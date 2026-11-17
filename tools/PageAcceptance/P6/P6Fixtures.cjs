/*
 * 檔案整體說明
 * 目的與定位：建立階段6四情境專用的合成資料與對照欄位，不讀取既有會友帳密。
 * 主要流程：以當次記憶體隨機帳密新增登入會友及小組，課程／週報／出席資料只參考
 * 核定測試教會與測試聯絡人，預先建立出席避免產品另建未追蹤記錄；兩輪使用相同值、
 * 不同正式ID，以ID映射做唯一身分正規化。回讀只含當次擁有的明列欄位。
 * 使用邊界與維護：SDK bridge唯一擁有資料並在reset／close逆序清理；本模組不Dispose租約、
 * 不保存cookie或token、不將登入帳密寫檔／輸出。不改任何既有contact的LINE綁定。
 * 規格與維護：欄位由goodshepherdback唯讀metadata核對，日期為同輪固定值；故障立即
 * 交上層finally清理，不重試Create，不以刪除同名資料清理、不偽造簽到成功。
 */
'use strict';
const { randomUUID } = require('node:crypto');

/** 每輪獨立ID與相同合成內容；login帳密由上層建立並只保存在記憶體。 */
async function createRoundFixture(bridge, settings, credentials) {
  const ids = Object.fromEntries(['login', 'group', 'lesson', 'stor', 'meeting', 'weekly', 'present'].map(x => [x, randomUUID()]));
  const lookup = (entity, id) => ({ lookup: entity, id });
  const create = (entity, id, fields) => bridge.request({ operation: 'create', entity, id, fields });
  const date = { date: settings.sunday };
  await create('contact', ids.login, {
    lastname: 'P6 Page Acceptance', parentcustomerid: lookup('account', settings.churchId),
    new_app_acount: credentials.account, new_app_pass: credentials.password
  });
  await create('list', ids.group, {
    listname: 'P6 Page Acceptance Group', createdfromcode: { option: 2 }, type: false,
    purpose: '小組名單', new_app_named: true, new_contact_family_leader_list: lookup('contact', ids.login)
  });
  for (const memberId of [ids.login, settings.contactId])
    await bridge.request({ operation: 'addMember', listId: ids.group, memberId });
  await create('new_disciple_lessons', ids.lesson, {
    new_name: 'P6 Page Acceptance Lesson', new_l1_name: 'P6 Lesson One', new_lessons_fee: { money: 0 },
    new_contact_new_disciple_lessons: lookup('contact', settings.contactId),
    new_list_new_disciple_lessons: lookup('list', ids.group)
  });
  await create('new_stor_lessons', ids.stor, {
    new_name: 'P6 Page Acceptance Enrollment', new_enroll_status: { option: 100000000 },
    new_new_disciple_lessons_new_stor_les: lookup('new_disciple_lessons', ids.lesson),
    new_contact_new_stor_lessons: lookup('contact', settings.contactId),
    new_account_new_stor_lessons: lookup('account', settings.churchId)
  });
  await create('new_meeting_statistics', ids.meeting, {
    new_name: 'P6 Page Acceptance Meeting', new_sunday_date: date, new_1_sign_on_name: 'P6 Meeting One'
  });
  await create('new_group_present_weekly_report', ids.weekly, {
    new_name: 'P6 Page Acceptance Weekly', new_sunday_date: date,
    new_list_group_present_weekly_report: lookup('list', ids.group),
    new_group_head_group_present_weekly_r: lookup('contact', ids.login)
  });
  await create('new_present_record', ids.present, {
    new_name: 'P6 Page Acceptance Attendance', new_sunday_date: date,
    new_contact_new_present_record: lookup('contact', settings.contactId),
    new_group_present_weekly_report_prese: lookup('new_group_present_weekly_report', ids.weekly),
    new_list_new_present_record: lookup('list', ids.group),
    new_group_present_this_week: 0, new_happy_present: 0, new_sunday_present_this_week: 0
  });
  return {
    ids, account: credentials.account, password: credentials.password, displayName: 'P6 Acceptance', lineUserId: settings.lineUserId,
    lessonQr: ids.lesson + '_1_On', meetingUnderscoreQr: ids.meeting + '_1_On',
    meetingAtQr: ids.meeting + '@new_sunday_first_qr@On', smallGroupQr: ids.weekly + '_On'
  };
}

/** 逐欄讀回當次明列寫入值；不讀帳密、既有會員或無關實體，日期值由對照端判定是否有寫。 */
async function readRoundFixture(bridge, fixture) {
  const definitions = [
    ['stor', 'new_stor_lessons', ['new_1_signon_time', 'new_1_present']],
    ['present', 'new_present_record', ['new_1_sign_on_time', 'new_sunday_first_qr_on_time', 'new_small_group_signing_time',
      'new_sunday_present_this_week', 'new_group_present_this_week', 'new_happy_present', 'new_meeting_statistics_new_present_re']],
    ['weekly', 'new_group_present_weekly_report', ['new_saved_flag']]
  ];
  const result = {};
  for (const [key, entity, fields] of definitions) result[key] = (await bridge.request({
    operation: 'retrieve', entity, id: fixture.ids[key], fields
  })).fields;
  return result;
}
module.exports = { createRoundFixture, readRoundFixture };
