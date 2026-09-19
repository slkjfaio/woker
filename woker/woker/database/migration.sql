-- ============================================================
-- 数据库迁移脚本：从旧结构迁移到支持项目管理的新结构
-- 使用方式: mysql -uroot -p workbench < migration.sql
-- ============================================================

USE `workbench`;

-- 1. 创建项目表
CREATE TABLE IF NOT EXISTS `projects` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`     BIGINT UNSIGNED NOT NULL,
  `name`        VARCHAR(128)    NOT NULL COMMENT '项目名称',
  `description` VARCHAR(512)    NOT NULL DEFAULT '' COMMENT '项目描述',
  `color`       VARCHAR(16)     NOT NULL DEFAULT '#0078D4' COMMENT '项目标识色',
  `is_default`  TINYINT(1)      NOT NULL DEFAULT 0 COMMENT '是否为默认项目',
  `is_archived` TINYINT(1)      NOT NULL DEFAULT 0 COMMENT '是否已归档',
  `created_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`  DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_project_user` (`user_id`),
  CONSTRAINT `fk_project_user` FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='项目管理';

-- 2. 为每个用户创建默认项目，并根据 project_name 分组创建项目
INSERT INTO `projects` (`user_id`, `name`, `description`, `is_default`)
SELECT DISTINCT
  `user_id`,
  CASE
    WHEN `project_name` = '' OR `project_name` IS NULL THEN '默认项目'
    ELSE `project_name`
  END as name,
  '从旧数据自动迁移' as description,
  1 as is_default
FROM `daily_logs`
WHERE NOT EXISTS (
  SELECT 1 FROM `projects` WHERE `projects`.`user_id` = `daily_logs`.`user_id`
)
GROUP BY `user_id`,
  CASE
    WHEN `project_name` = '' OR `project_name` IS NULL THEN '默认项目'
    ELSE `project_name`
  END;

-- 3. 添加临时列用于迁移
ALTER TABLE `daily_logs` ADD COLUMN `project_id_temp` BIGINT UNSIGNED NULL AFTER `user_id`;

-- 4. 将 project_name 映射到 project_id
UPDATE `daily_logs` dl
INNER JOIN `projects` p ON dl.`user_id` = p.`user_id`
  AND (
    (dl.`project_name` = '' OR dl.`project_name` IS NULL) AND p.`name` = '默认项目'
    OR dl.`project_name` = p.`name`
  )
SET dl.`project_id_temp` = p.`id`;

-- 5. 删除旧的唯一索引
ALTER TABLE `daily_logs` DROP INDEX `ux_log_user_date`;

-- 6. 删除旧的 project_name 列
ALTER TABLE `daily_logs` DROP COLUMN `project_name`;

-- 7. 将临时列重命名为 project_id
ALTER TABLE `daily_logs` CHANGE COLUMN `project_id_temp` `project_id` BIGINT UNSIGNED NOT NULL;

-- 8. 添加新的外键约束和唯一索引
ALTER TABLE `daily_logs`
  ADD CONSTRAINT `fk_log_project` FOREIGN KEY (`project_id`) REFERENCES `projects`(`id`) ON DELETE CASCADE,
  ADD UNIQUE KEY `ux_log_user_project_date` (`user_id`, `project_id`, `log_date`),
  ADD KEY `ix_log_project` (`project_id`);

-- 9. 确保每个用户都有至少一个默认项目
INSERT INTO `projects` (`user_id`, `name`, `description`, `is_default`)
SELECT DISTINCT u.`id`, '默认项目', '自动创建的默认项目', 1
FROM `users` u
WHERE NOT EXISTS (
  SELECT 1 FROM `projects` p WHERE p.`user_id` = u.`id`
);

-- 迁移完成
SELECT '数据库迁移完成！' as status;
