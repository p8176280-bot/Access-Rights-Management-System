-- ข้อมูลตัวอย่าง ระบบขอสิทธิใช้งานโปรแกรม (บริษัท โตโยต้า)
-- รันหลัง schema-postgresql.sql และหลังเปิดแอปครั้งแรก (แอปจะสร้างบัญชี Admin ให้อัตโนมัติ)
-- ผู้อนุมัติของทุกสิทธิ = ผู้ดูแลระบบคนแรกในระบบ
-- หมายเหตุ: ไม่ใส่รหัสผ่านของผู้ใช้ไว้ในไฟล์นี้ บัญชีผู้ใช้ต้องสร้างผ่านหน้า "จัดการผู้ใช้" ในโหมดผู้ดูแลระบบ

-- ให้ Admin คนแรกเป็นผู้อนุมัติได้ด้วย
INSERT INTO user_roles (user_id, role)
SELECT u.id, 'Approver'
FROM users u
WHERE u.id = (SELECT MIN(user_id) FROM user_roles WHERE role = 'Admin')
ON CONFLICT DO NOTHING;

-- รายการสิทธิ/โปรแกรมที่ขอได้
INSERT INTO rights (name, description, approver_id, max_duration_days)
SELECT v.name, v.descr, u.id, v.days
FROM users u,
(VALUES
  ('ระบบ SAP - ดูรายงาน',      'ดูรายงานการขายและสต็อก',              NULL),
  ('ระบบ SAP - บันทึกข้อมูล',   'บันทึก/แก้ไขเอกสารในระบบ SAP',         180),
  ('ระบบ HR - ข้อมูลพนักงาน',   'ดูข้อมูลพนักงาน',                      90),
  ('VPN เข้าถึงจากภายนอก',      'ใช้เครือข่ายบริษัทจากนอกสำนักงาน',      365),
  ('Microsoft 365 / Outlook',   'อีเมลและเอกสารออนไลน์',               NULL)
) AS v(name, descr, days)
WHERE u.id = (SELECT MIN(user_id) FROM user_roles WHERE role = 'Admin')
ON CONFLICT (name) DO NOTHING;
