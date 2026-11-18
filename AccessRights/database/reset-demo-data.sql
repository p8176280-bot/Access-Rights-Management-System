-- =====================================================================
--  ลบข้อมูลตัวอย่างออกจากระบบ (PostgreSQL)
--  - ลบคำขอ ประวัติ และแจ้งเตือน "ทั้งหมด" (เป็นข้อมูลจากการทดสอบ)
--  - ลบสิทธิตัวอย่าง: ERP - ดูข้อมูล, ERP - บันทึกรายการ, HR - ข้อมูลพนักงาน, VPN
--  - ลบบัญชีตัวอย่าง: approver1, approver2, user1, user2
--  บัญชีอื่น (เช่น non) และสิทธิที่ Admin เพิ่มเองจะไม่ถูกลบ
--
--  ⚠ ลบแล้วกู้คืนไม่ได้
--  รันใน pgAdmin: คลิกขวา access_rights → Query Tool → เปิดไฟล์นี้ → กด ▶ (F5)
-- =====================================================================

BEGIN;

-- ตารางประวัติห้ามลบทีละแถว (BR-10) จึงล้างด้วย TRUNCATE ซึ่งใช้เฉพาะตอนล้างข้อมูลทดสอบ
TRUNCATE notifications, request_histories, access_requests RESTART IDENTITY;

DELETE FROM rights
WHERE name IN ('ERP - ดูข้อมูล', 'ERP - บันทึกรายการ', 'HR - ข้อมูลพนักงาน', 'VPN');

DELETE FROM users
WHERE username IN ('approver1', 'approver2', 'user1', 'user2');

COMMIT;

-- ตรวจผล: ควรเหลือเฉพาะบัญชีจริง
SELECT u.username, u.full_name, string_agg(r.role, ', ' ORDER BY r.role) AS roles
FROM users u LEFT JOIN user_roles r ON r.user_id = u.id
GROUP BY u.id ORDER BY u.username;
