/*
 * 檔案整體說明
 * 目的與定位：P6與P8頁面驗收共用的單一SDK資料owner，只連部署白名單goodshepherdback。
 * 主要功能與流程：以嚴格UTF-8逐行JSON標準輸入處理本輪合成fixture；Create先登記自行產生的ID，Update／Delete／
 * Retrieve只接受本程序已登記ID，拒絕更改既有會員。Query只列本程序ID，不接受任意FetchXML。
 * 使用邊界與維護：組態與連線租約在Main持有，最多128筆記錄、256次命令，每行64KiB；finally逆序清理
 * 已建立記錄並Dispose租約／組態、解除組件解析。失敗只輸出固定類型，不回原例外或秘密。
 * 偽語系租約僅能讀寫User Secrets綁定的測試教會new_enabledlanguages：保存原值、只追加qps-ploc，
 * 回讀確認後才回報；結束時確認欄位仍等於本程序寫入值才還原，避免覆蓋外部並行變更。
 * prepareSecrets只在核定後使用：原始Host秘密以DPAPI CurrentUser備份到repo外，驗證後
 * 直接由記憶體寫入測試token至指定User Secrets；finally逐位元組還原或刪除原本不存在的檔案。
 * 保留受保護備份作復原證據，restore失敗明標，不修改產品安全守門。
 * 登入帳密只從stdin記憶體接收並寫入本輪新會員，不寫log。SDK回應中的帳密、LINE ID、
 * 基底身分值均不回傳；只回選定合成欄位。EOF也執行清理，殘留必須為0才能驗收。
 */
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

/// <summary>單次JSON管線與SDK資源owner，所有可變狀態限本程序且不接受任意組織。</summary>
internal static class P6FixtureOwner
{
    /// <summary>本程序擁有的精確ID；先確認未存在再登記，Create回應不確定時亦保留，清理不可誤刪他人記錄。</summary>
    /// <param name="Entity">固定白名單實體邏輯名稱。</param>
    /// <param name="Id">本輪預先產生且確認不存在的正式ID。</param>
    private sealed record Owned(string Entity, Guid Id);

    /// <summary>有界命令迴圈；SDK同步操作返回前不釋放租約，所有成功建立的資料在EOF逆序刪除。</summary>
    internal static int Main()
    {
        Console.InputEncoding=new UTF8Encoding(false,true);
        Console.OutputEncoding=new UTF8Encoding(false);
        var bin=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"SpeechMessage.Dynamics.Deployment/bin/Release/net10.0"));
        Assembly? Resolve(AssemblyLoadContext c,AssemblyName n){var p=Path.Combine(bin,n.Name+".dll");return File.Exists(p)?c.LoadFromAssemblyPath(p):null;}
        var context=AssemblyLoadContext.Default;context.Resolving+=Resolve;
        object? configuration=null,lease=null;IOrganizationService? sdk=null;var owned=new List<Owned>();var exit=0;
        string? secretsPath=null,backupPath=null;byte[]? originalSecrets=null;bool originalExisted=false,secretsChanged=false;
        Guid testChurchId=Guid.Empty;string? originalEnabledLanguages=null,leasedEnabledLanguages=null;
        bool pseudoLocaleLeaseActive=false,pseudoLocaleRestored=true;
        try
        {
            var assembly=context.LoadFromAssemblyPath(Path.Combine(bin,"SpeechMessage.Dynamics.Deployment.dll"));
            const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
            configuration=assembly.GetType("SpeechMessage.Dynamics.Deployment.DeploymentConfiguration",true)!.GetMethod("Create",flags)!.Invoke(null,new object[]{assembly})!;
            var connectorType=assembly.GetType("SpeechMessage.Dynamics.Deployment.UserSecretsOrganizationConnector",true)!;
            var connector=Activator.CreateInstance(connectorType,new object[]{configuration})!;
            lease=connectorType.GetMethod("Connect")!.Invoke(connector,new object[]{"goodshepherdback"})!;
            sdk=(IOrganizationService)lease.GetType().GetProperty("Service")!.GetValue(lease)!;
            var indexer=configuration.GetType().GetProperty("Item")!;
            const string prefix="Deployment:Organizations:goodshepherdback:";
            string Setting(string key)=>(string?)indexer.GetValue(configuration,new object[]{prefix+key})??throw new InvalidDataException("Test setting missing.");
            var churchId=Guid.Parse(Setting("ShadowTestChurchId"));
            testChurchId=churchId;
            var contactId=Guid.Parse(Setting("ShadowTestContactId"));
            assembly.GetType("SpeechMessage.Dynamics.Deployment.Shadow.ShadowTestIdentity",true)!
                .GetMethod("Verify",flags)!.Invoke(null,new object[]{sdk,Setting("TestLineUserId"),churchId.ToString(),contactId.ToString(),CancellationToken.None});
            Console.WriteLine("{\"ready\":true}");
            for(var number=0;number<256;number++)
            {
                var line=ReadBoundedLine();if(line is null)break;
                using var document=JsonDocument.Parse(line);var request=document.RootElement;
                var operation=request.GetProperty("operation").GetString();
                try
                {
                    if(operation=="auditResidue")
                    {
                        var counts=new Dictionary<string,int>();
                        foreach(var name in new[]{"contact","list","new_disciple_lessons","new_stor_lessons","new_meeting_statistics","new_group_present_weekly_report","new_present_record"})
                        {
                            var field=name=="contact"?"lastname":name=="list"?"listname":"new_name";
                            var query=new QueryExpression(name){ColumnSet=new ColumnSet(false),TopCount=101};
                            query.Criteria.AddCondition(field,ConditionOperator.BeginsWith,"P6 Page Acceptance");
                            var rows=sdk.RetrieveMultiple(query);
                            if(rows.MoreRecords || rows.Entities.Count>100)throw new InvalidDataException("Residue inventory bound rejected.");
                            counts.Add(name,rows.Entities.Count);
                        }
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,counts}));continue;
                    }
                    if(operation=="prepareSecrets")
                    {
                        if(secretsChanged)throw new InvalidDataException("Secrets already leased.");
                        secretsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft/UserSecrets/SpeechMessageProducts.ChurchReport.Development/secrets.json");
                        originalExisted=File.Exists(secretsPath);
                        originalSecrets=originalExisted?File.ReadAllBytes(secretsPath):Array.Empty<byte>();
                        if(originalSecrets.Length>65536)throw new InvalidDataException("Secrets backup bound rejected.");
                        var backupRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".speechmessage/evidence/p6-secrets");
                        Directory.CreateDirectory(backupRoot);backupPath=Path.Combine(backupRoot,"churchreport-"+Guid.NewGuid().ToString("N")+".dpapi");
                        var protectedBytes=ProtectedData.Protect(originalSecrets,null,DataProtectionScope.CurrentUser);
                        using(var file=new FileStream(backupPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(protectedBytes);file.Flush(true);}
                        Array.Clear(protectedBytes);
                        var verified=ProtectedData.Unprotect(File.ReadAllBytes(backupPath),null,DataProtectionScope.CurrentUser);
                        if(!verified.SequenceEqual(originalSecrets))throw new InvalidDataException("Protected backup mismatch.");
                        Array.Clear(verified);
                        var values=originalSecrets.Length==0?new JsonObject():JsonNode.Parse(Encoding.UTF8.GetString(originalSecrets).TrimStart('\uFEFF'),new JsonNodeOptions(),new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true})!.AsObject();
                        values["LineMessaging:DevTest:ChannelAccessToken"]=Setting("TestChannelToken");
                        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
                        secretsChanged=true;
                        File.WriteAllText(secretsPath,values.ToJsonString(),new UTF8Encoding(false));
                        Console.WriteLine("{\"ok\":true,\"backupProtected\":true,\"backupVerified\":true}");continue;
                    }
                    if(operation=="leasePseudoLocale")
                    {
                        if(pseudoLocaleLeaseActive)throw new InvalidDataException("Pseudo locale lease already active.");
                        var account=sdk.Retrieve("account",churchId,new ColumnSet("new_enabledlanguages"));
                        originalEnabledLanguages=account.GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                        var enabled=originalEnabledLanguages.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
                        var alreadyEnabled=enabled.Any(x=>string.Equals(x,"qps-ploc",StringComparison.OrdinalIgnoreCase));
                        leasedEnabledLanguages=alreadyEnabled?originalEnabledLanguages:
                            (string.IsNullOrWhiteSpace(originalEnabledLanguages)?"qps-ploc":originalEnabledLanguages.TrimEnd()+",qps-ploc");
                        if(!alreadyEnabled)
                        {
                            leasedEnabledLanguages=string.IsNullOrWhiteSpace(originalEnabledLanguages)
                                ?"qps-ploc":originalEnabledLanguages.TrimEnd()+",qps-ploc";
                            pseudoLocaleLeaseActive=true;
                            pseudoLocaleRestored=false;
                            var update=new Entity("account",churchId){["new_enabledlanguages"]=leasedEnabledLanguages};
                            sdk.Update(update);
                            var verify=sdk.Retrieve("account",churchId,new ColumnSet("new_enabledlanguages"))
                                .GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                            if(!string.Equals(verify,leasedEnabledLanguages,StringComparison.Ordinal))
                                throw new InvalidDataException("Pseudo locale lease verification failed.");
                        }
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,enabled=true,changed=!alreadyEnabled}));continue;
                    }
                    if(operation=="restorePseudoLocale")
                    {
                        if(pseudoLocaleLeaseActive)
                        {
                            var account=sdk.Retrieve("account",churchId,new ColumnSet("new_enabledlanguages"));
                            var current=account.GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                            if(!string.Equals(current,leasedEnabledLanguages,StringComparison.Ordinal))
                                throw new InvalidDataException("Pseudo locale lease changed outside owner.");
                            sdk.Update(new Entity("account",churchId){["new_enabledlanguages"]=originalEnabledLanguages});
                            var verify=sdk.Retrieve("account",churchId,new ColumnSet("new_enabledlanguages"))
                                .GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                            if(!string.Equals(verify,originalEnabledLanguages,StringComparison.Ordinal))
                                throw new InvalidDataException("Pseudo locale restore verification failed.");
                            pseudoLocaleLeaseActive=false;
                        }
                        pseudoLocaleRestored=true;
                        Console.WriteLine("{\"ok\":true,\"restored\":true}");continue;
                    }
                    if(operation=="addMember")
                    {
                        var listId=Guid.Parse(request.GetProperty("listId").GetString()!);
                        var memberId=Guid.Parse(request.GetProperty("memberId").GetString()!);
                        if(!owned.Contains(new Owned("list",listId)) || (memberId!=contactId&&!owned.Contains(new Owned("contact",memberId))))
                            throw new InvalidDataException("Member scope rejected.");
                        sdk.Execute(new AddMemberListRequest{ListId=listId,EntityId=memberId});
                        Console.WriteLine("{\"ok\":true}");continue;
                    }
                    if(operation=="reset")
                    {
                        foreach(var entry in owned.AsEnumerable().Reverse())sdk.Delete(entry.Entity,entry.Id);
                        foreach(var entry in owned)
                        {
                            var query=new QueryExpression(entry.Entity){ColumnSet=new ColumnSet(false),TopCount=1};
                            query.Criteria.AddCondition(entry.Entity+"id",ConditionOperator.Equal,entry.Id);
                            if(sdk.RetrieveMultiple(query).Entities.Count!=0)throw new InvalidDataException("Fixture drain failed.");
                        }
                        owned.Clear();Console.WriteLine("{\"ok\":true,\"remaining\":0}");continue;
                    }
                    if(operation=="metadata")
                    {
                        var name=request.GetProperty("entity").GetString()!;
                        if(!new[]{"contact","list","new_disciple_lessons","new_stor_lessons","new_meeting_statistics","new_group_present_weekly_report","new_present_record"}.Contains(name))throw new InvalidDataException("Entity rejected.");
                        var metadata=(RetrieveEntityResponse)sdk.Execute(new RetrieveEntityRequest{LogicalName=name,EntityFilters=EntityFilters.Attributes});
                        var rows=metadata.EntityMetadata.Attributes.Where(a=>
                                name=="contact"
                                    ? a.LogicalName=="new_church_jobtitle"
                                    : a.IsValidForCreate==true)
                            .Select(a=>new {name=a.LogicalName,type=a.AttributeType?.ToString(),
                                isValidForCreate=a.IsValidForCreate,isValidForUpdate=a.IsValidForUpdate,
                                targets=(a as LookupAttributeMetadata)?.Targets});
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,attributes=rows}));continue;
                    }
                    if(operation=="finish")break;
                    var entity=request.GetProperty("entity").GetString()!;
                    var id=request.TryGetProperty("id",out var inputId)?Guid.Parse(inputId.GetString()!):Guid.NewGuid();
                    var item=new Owned(entity,id);
                    if(operation=="create")
                    {
                        if(owned.Count>=128||owned.Contains(item))throw new InvalidDataException("Owner bound rejected.");
                        if(!new[]{"contact","list","new_disciple_lessons","new_stor_lessons","new_meeting_statistics","new_group_present_weekly_report","new_present_record"}.Contains(entity))throw new InvalidDataException("Entity rejected.");
                        var preexisting=new QueryExpression(entity){ColumnSet=new ColumnSet(false),TopCount=1};
                        preexisting.Criteria.AddCondition(entity+"id",ConditionOperator.Equal,id);
                        if(sdk.RetrieveMultiple(preexisting).Entities.Count!=0)throw new InvalidDataException("Existing ID rejected.");
                        var value=ReadEntity(request,entity,id,owned,churchId,contactId);
                        owned.Add(item);sdk.Create(value);
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,id}));continue;
                    }
                    if(!owned.Contains(item))throw new InvalidDataException("Unowned ID rejected.");
                    if(operation=="update")
                    {
                        var update=ReadEntity(request,entity,id,owned,churchId,contactId);
                        sdk.Update(update);
                        if(entity=="contact"&&update.Contains("new_church_jobtitle"))
                        {
                            var persisted=sdk.Retrieve("contact",id,new ColumnSet("new_church_jobtitle"))
                                .GetAttributeValue<string>("new_church_jobtitle");
                            var matches=string.Equals(persisted,update.GetAttributeValue<string>("new_church_jobtitle"),StringComparison.Ordinal);
                            Console.WriteLine(JsonSerializer.Serialize(new{ok=true,roleValuePersisted=matches}));continue;
                        }
                        Console.WriteLine("{\"ok\":true}");continue;
                    }
                    if(operation=="retrieve")
                    {
                        var fields=request.GetProperty("fields").EnumerateArray().Select(x=>x.GetString()!).ToArray();
                        if(fields.Any(x=>x is "new_app_acount" or "new_app_pass" or "new_lineid"))throw new InvalidDataException("Secret field rejected.");
                        var value=sdk.Retrieve(entity,id,new ColumnSet(fields));
                        var result=fields.ToDictionary(x=>x,x=>value.Contains(x)?Project(value[x]):null);
                        Console.WriteLine(JsonSerializer.Serialize(new{ok=true,fields=result}));continue;
                    }
                    throw new InvalidDataException("Operation rejected.");
                }
                catch(Exception e)
                {
                    while(e is TargetInvocationException w && w.InnerException is Exception inner)e=inner;
                    Console.WriteLine(JsonSerializer.Serialize(new{ok=false,error=e.GetType().Name,code=(e as System.ServiceModel.FaultException<OrganizationServiceFault>)?.Detail.ErrorCode}));exit=1;
                }
            }
        }
        catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{ok=false,error=e.GetType().Name,code=(e as System.ServiceModel.FaultException<OrganizationServiceFault>)?.Detail.ErrorCode}));exit=1;}
        finally
        {
            var remaining=0;
            if(pseudoLocaleLeaseActive)
            {
                try
                {
                    var account=sdk!.Retrieve("account",testChurchId,new ColumnSet("new_enabledlanguages"));
                    var current=account.GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                    if(string.Equals(current,leasedEnabledLanguages,StringComparison.Ordinal))
                    {
                        sdk.Update(new Entity("account",testChurchId){["new_enabledlanguages"]=originalEnabledLanguages});
                        var verify=sdk.Retrieve("account",testChurchId,new ColumnSet("new_enabledlanguages"))
                            .GetAttributeValue<string>("new_enabledlanguages")??string.Empty;
                        if(!string.Equals(verify,originalEnabledLanguages,StringComparison.Ordinal))
                            throw new InvalidDataException("Pseudo locale restore verification failed.");
                    }
                    else if(!string.Equals(current,originalEnabledLanguages,StringComparison.Ordinal))
                        throw new InvalidDataException("Pseudo locale lease changed outside owner.");
                    pseudoLocaleLeaseActive=false;pseudoLocaleRestored=true;
                }
                catch{pseudoLocaleRestored=false;exit=1;}
            }
            if(sdk is not null)foreach(var item in owned.AsEnumerable().Reverse())
            {
                // ID先由此程序自行產生；即使Create失敗亦只嘗試清理該ID，絕不按名稱刪既有資料。
                try
                {
                    sdk.Delete(item.Entity,item.Id);
                    var query=new QueryExpression(item.Entity){ColumnSet=new ColumnSet(false),TopCount=1};
                    query.Criteria.AddCondition(item.Entity+"id",ConditionOperator.Equal,item.Id);
                    if(sdk.RetrieveMultiple(query).Entities.Count!=0){remaining++;exit=1;}
                }catch(System.ServiceModel.FaultException<OrganizationServiceFault> e)when(e.Detail.ErrorCode==unchecked((int)0x80040217)){}
                catch{remaining++;exit=1;}
            }
            var secretsRestored=!secretsChanged;
            if(secretsChanged)
            {
                try
                {
                    var restored=ProtectedData.Unprotect(File.ReadAllBytes(backupPath!),null,DataProtectionScope.CurrentUser);
                    if(!restored.SequenceEqual(originalSecrets!))throw new InvalidDataException("Backup changed.");
                    if(originalExisted)File.WriteAllBytes(secretsPath!,restored);else if(File.Exists(secretsPath))File.Delete(secretsPath);
                    secretsRestored=originalExisted?File.ReadAllBytes(secretsPath!).SequenceEqual(restored):!File.Exists(secretsPath);
                    Array.Clear(restored);
                    if(!secretsRestored)exit=1;
                }
                catch{secretsRestored=false;exit=1;}
            }
            if(originalSecrets is not null)Array.Clear(originalSecrets);
            try{(lease as IDisposable)?.Dispose();}catch{exit=1;}
            try{(configuration as IDisposable)?.Dispose();}catch{exit=1;}
            context.Resolving-=Resolve;Console.WriteLine(JsonSerializer.Serialize(new{cleanup=exit==0,remaining,secretsRestored,
                pseudoLocaleRestored,backupProtected=backupPath is not null}));
        }
        return exit;
    }

    /// <summary>在讀取階段即限制每行64KiB，避免先ReadLine再檢查造成無界輸入保留；EOF後不再接受命令。</summary>
    private static string? ReadBoundedLine()
    {
        var line=new StringBuilder();
        for(var count=0;count<=65536;count++)
        {
            var next=Console.Read();
            if(next<0)return line.Length==0?null:line.ToString();
            if(next=='\n')return line.ToString();
            if(next!='\r')line.Append((char)next);
        }
        throw new InvalidDataException("Command too large.");
    }

    /// <summary>僅從本輪JSON建立純SDK候選；lookup受fixture上層測試教會／會友範圍約束。</summary>
    private static Entity ReadEntity(JsonElement request,string name,Guid id,IReadOnlyList<Owned> owned,Guid churchId,Guid contactId)
    {
        var entity=new Entity(name,id);
        foreach(var field in request.GetProperty("fields").EnumerateObject())
        {
            var v=field.Value;
            entity[field.Name]=v.ValueKind switch
            {
                JsonValueKind.Null=>null,
                JsonValueKind.True=>true,
                JsonValueKind.False=>false,
                JsonValueKind.String=>v.GetString(),
                JsonValueKind.Number=>v.GetInt32(),
                JsonValueKind.Object when v.TryGetProperty("lookup",out var target)=>new EntityReference(target.GetString()!,Guid.Parse(v.GetProperty("id").GetString()!)),
                JsonValueKind.Object when v.TryGetProperty("option",out var option)=>new OptionSetValue(option.GetInt32()),
                JsonValueKind.Object when v.TryGetProperty("date",out var date)=>DateTime.Parse(date.GetString()!,null,System.Globalization.DateTimeStyles.RoundtripKind),
                JsonValueKind.Object when v.TryGetProperty("money",out var money)=>new Money(money.GetDecimal()),
                _=>throw new InvalidDataException("Value rejected.")
            };
        }
        foreach(var reference in entity.Attributes.Values.OfType<EntityReference>())
            if(!(reference.LogicalName=="account"&&reference.Id==churchId)
                && !(reference.LogicalName=="contact"&&reference.Id==contactId)
                && !owned.Contains(new Owned(reference.LogicalName,reference.Id)))
                throw new InvalidDataException("Lookup outside test scope rejected.");
        if(name=="contact" && request.GetProperty("operation").GetString()=="create"
            && !(entity.GetAttributeValue<EntityReference>("parentcustomerid") is {LogicalName:"account"} parent && parent.Id==churchId))
            throw new InvalidDataException("Test contact church required.");
        if(entity.Contains("new_lineid"))throw new InvalidDataException("New LINE binding rejected.");
        return entity;
    }

    /// <summary>將合成讀回資料投影為純值；日期保留標記供上層只比較是否寫入，lookup保留正式ID。</summary>
    private static object? Project(object? value)=>value switch
    {
        EntityReference r=>new{lookup=r.LogicalName,id=r.Id},OptionSetValue o=>o.Value,Money m=>m.Value,
        DateTime d=>new{date=d.ToString("O")},null=>null,_=>value
    };
}
