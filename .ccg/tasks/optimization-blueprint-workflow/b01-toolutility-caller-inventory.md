# B01 ToolUtility Caller Inventory

## Evidence Status

- Issue: `B01-SEC-003`
- Evidence class: source/package caller inventory
- Capture date: 2026-07-18
- Result: `SOURCE_INVENTORY_COMPLETE_RUNTIME_CLASSIFICATION_PENDING`
- Redaction: this file contains only caller path, owner, and `KEY`/`RAW`
  classification. It contains no account, contact, credential, hash, key,
  session, claim, response, or CRM payload value.

The current account-login path writes the submitted password into session and
manager state and passes it through `SetupSystemData`. Therefore every direct
business caller below is classified `RAW` in the current source baseline. The
B01 repair target is `KEY`; that target cannot be recorded until the synthetic
route probe proves the same deployed paths receive the B01 compatibility key.

## Direct Business Callers

| Caller path | Owner | Classification |
|---|---|---|
| `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs:610` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs:509` | B02 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/AppointmentsDownUpLoader.cs:130` | B04B | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/ChurchListDataProcessor.cs:144` | B06A | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadEquipment.cs:106` | B04B | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadHappyGroup.cs:196` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs:157` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs:501` | B06A | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs:141` | B06B | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/HappyGroupUtility.cs:85` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs:448` | B02 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/NewPerson.cs:726` | B02 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs:177` | B02 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/PersonalInfomatioManager.cs:420` | B02 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Core.cs:301` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.Core.cs:339` | B03 | RAW |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/WeeklyReportManager.cs:377` | B03 | RAW |

Owner totals: B02=5, B03=7, B04B=2, B06A=2, B06B=1; total=17.

## ToolUtility Boundary

| Caller path | Owner | Classification |
|---|---|---|
| `ToolUtility/ToolUtilityStaticGlobal.cs:83` | F03A | RAW |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs:55` | F03A | RAW |
| `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Contact.cs:61` | F03A | RAW |
| `ToolUtility/Core/ToolUtilityFacade.cs:382` | F03Q | RAW |
| `ToolUtility/Core/ToolUtilityFacade.cs:395` | F03Q | RAW |
| `ToolUtility/ContactOperations/ContactService.cs:218` | F03A | RAW |

No repository caller of `IContactService.AccountLogin` was found. Its deployed
package status still requires owner confirmation; absence from source search is
not proof that an external binary consumer does not exist.

## Package Inclusion Proof

- `SpeechMessageProducts.ChurchReport.csproj` uses default SDK compile inclusion
  and removes only `文件/佈署規劃/**` from compilation.
- The ChurchReport project references `ToolUtility/ToolUtility.csproj`.
- All 17 direct business paths are therefore source-included in the current
  ChurchReport build. A successful local build proves compilation, not deployed
  environment identity or runtime `KEY` propagation.

Local verification on 2026-07-18:

- repository search versus this table: actual=17, documented=17, missing=0,
  extra=0;
- `dotnet build SpeechMessageProducts.ChurchReport.csproj --no-restore`: passed
  with 0 warnings and 0 errors;
- `git diff --check`: passed.

## Remaining Gate

This source inventory satisfies only the repository caller-list portion of the
contract. B01 remains blocked until all of the following are available:

1. F03A/CRM owner confirmation that no external deployed binary caller of the
   three account APIs requires raw password compatibility.
2. Non-production row-version conditional-update success/conflict evidence.
3. Synthetic `ProcessLogin -> SetupSystemData` success/failure proof showing
   the relevant B02/B03/B04B/B06A/B06B paths receive `KEY`, not `RAW`.
