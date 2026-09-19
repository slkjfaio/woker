-- ============================================================
-- 工作日志工作台 WorkBench · MySQL 建库脚本
-- 使用方式: mysql -uroot -p < schema.sql
-- 默认库: workbench (utf8mb4)
-- ============================================================

CREATE DATABASE IF NOT EXISTS `workbench`
  DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE `workbench`;

-- ------------------------------------------------------------
-- 用户表（账号密码认证）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `users` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `username`      VARCHAR(128)  NOT NULL COMMENT '登录名',
  `password_hash` VARCHAR(512)  NOT NULL COMMENT 'PBKDF2 散列',
  `display_name`  VARCHAR(128)  NOT NULL DEFAULT '',
  `email`         VARCHAR(256)  NULL,
  `created_at`    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_login_at` DATETIME      NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_users_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='用户';

-- ------------------------------------------------------------
-- 项目管理表（用户可创建多个项目，实现数据隔离）
-- ------------------------------------------------------------
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

-- ------------------------------------------------------------
-- 日报主表（用户 + 项目 + 日期唯一，支持多项目数据隔离）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `daily_logs` (
  `id`           BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`      BIGINT UNSIGNED NOT NULL,
  `project_id`   BIGINT UNSIGNED NOT NULL COMMENT '所属项目ID',
  `log_date`     DATE            NOT NULL,
  `created_at`   DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at`   DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_log_user_project_date` (`user_id`, `project_id`, `log_date`),
  KEY `ix_log_project` (`project_id`),
  CONSTRAINT `fk_log_user` FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE,
  CONSTRAINT `fk_log_project` FOREIGN KEY (`project_id`) REFERENCES `projects`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='日报主表';

-- ------------------------------------------------------------
-- 模块1 · 今日工作成果
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `work_tasks` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`     BIGINT UNSIGNED NOT NULL,
  `title`      VARCHAR(512) NOT NULL,
  `result`     TEXT         NULL COMMENT '量化结果描述',
  `type`       VARCHAR(64)  NOT NULL DEFAULT '其他',
  `cost_hours` DECIMAL(5,1) NOT NULL DEFAULT 0,
  `exec_count` INT          NOT NULL DEFAULT 0 COMMENT '执行用例数',
  `pass_count` INT          NOT NULL DEFAULT 0 COMMENT '通过用例数',
  `bug_count`  INT          NOT NULL DEFAULT 0 COMMENT '发现缺陷数',
  `is_done`    TINYINT(1)   NOT NULL DEFAULT 0,
  `sort_order` INT          NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_task_log` (`log_id`),
  CONSTRAINT `fk_task_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='今日工作成果';

-- ------------------------------------------------------------
-- 模块2 · 问题复盘与解决方案
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `reflections` (
  `id`            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`        BIGINT UNSIGNED NOT NULL,
  `title`         VARCHAR(512) NOT NULL,
  `category`      VARCHAR(64)  NOT NULL DEFAULT '其他',
  `time_str`      VARCHAR(16)  NOT NULL DEFAULT '' COMMENT '发生时刻 HH:mm',
  `phenomenon`    TEXT NULL COMMENT '1 问题现象',
  `investigation` TEXT NULL COMMENT '2 排查过程',
  `solution`      TEXT NULL COMMENT '3 落地方案',
  `prevention`    TEXT NULL COMMENT '4 优化规避',
  `sort_order`    INT  NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_reflect_log` (`log_id`),
  CONSTRAINT `fk_reflect_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='问题复盘';

-- ------------------------------------------------------------
-- 模块3 · 当日能力成长（五维度记录）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `growth_entries` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`     BIGINT UNSIGNED NOT NULL,
  `dim_key`    VARCHAR(32) NOT NULL COMMENT 'business/thinking/debug/tool/comm',
  `time_str`   VARCHAR(16) NOT NULL DEFAULT '',
  `content`    TEXT        NOT NULL,
  `sort_order` INT         NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_growth_log` (`log_id`),
  CONSTRAINT `fk_growth_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='能力成长记录';

-- 成长维度等级（每用户每维度一个累计等级 0-100）
CREATE TABLE IF NOT EXISTS `dim_levels` (
  `id`      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` BIGINT UNSIGNED NOT NULL,
  `dim_key` VARCHAR(32) NOT NULL,
  `level`   INT NOT NULL DEFAULT 50,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_dim_user` (`user_id`, `dim_key`),
  CONSTRAINT `fk_dim_user` FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='成长维度等级';

-- ------------------------------------------------------------
-- 模块4 · 遗留待办（pending/retest/doc/connect 四类）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `todos` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`     BIGINT UNSIGNED NOT NULL,
  `category`   VARCHAR(16) NOT NULL COMMENT 'pending/retest/doc/connect',
  `title`      VARCHAR(512) NOT NULL,
  `priority`   VARCHAR(16) NOT NULL DEFAULT 'medium' COMMENT 'high/medium/low',
  `deadline`   VARCHAR(32) NOT NULL DEFAULT '—',
  `meta`       VARCHAR(128) NOT NULL DEFAULT '—',
  `is_done`    TINYINT(1)  NOT NULL DEFAULT 0,
  `sort_order` INT         NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_todo_log` (`log_id`),
  CONSTRAINT `fk_todo_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='遗留待办';

-- ------------------------------------------------------------
-- 模块5 · 次日优先级计划（prio 0紧急优先/1常规迭代/2能力提升）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `plans` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`     BIGINT UNSIGNED NOT NULL,
  `prio`       TINYINT   NOT NULL DEFAULT 1,
  `title`      VARCHAR(512) NOT NULL,
  `note`       VARCHAR(512) NOT NULL DEFAULT '—',
  `est_hours`  DECIMAL(5,1) NOT NULL DEFAULT 1,
  `sort_order` INT       NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_plan_log` (`log_id`),
  CONSTRAINT `fk_plan_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='次日计划';

-- ------------------------------------------------------------
-- 模块6 · 周/转正总结素材库
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `materials` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `log_id`     BIGINT UNSIGNED NOT NULL,
  `category`   VARCHAR(32) NOT NULL DEFAULT '核心成果' COMMENT '核心成果/问题改进/工作亮点',
  `icon`       VARCHAR(32) NOT NULL DEFAULT 'target',
  `date_label` VARCHAR(16) NOT NULL DEFAULT '本日',
  `content`    TEXT        NOT NULL,
  `is_picked`  TINYINT(1)  NOT NULL DEFAULT 0,
  `sort_order` INT         NOT NULL DEFAULT 0,
  PRIMARY KEY (`id`),
  KEY `ix_mat_log` (`log_id`),
  CONSTRAINT `fk_mat_log` FOREIGN KEY (`log_id`) REFERENCES `daily_logs`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='总结素材';

-- ------------------------------------------------------------
-- 大模型配置（OpenAI 兼容协议，每用户可多套）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `llm_configs` (
  `id`          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`     BIGINT UNSIGNED NOT NULL,
  `name`        VARCHAR(128) NOT NULL COMMENT '显示名',
  `base_url`    VARCHAR(512) NOT NULL COMMENT '如 https://api.deepseek.com/v1',
  `api_key`     VARCHAR(512) NOT NULL DEFAULT '',
  `model_name`  VARCHAR(128) NOT NULL COMMENT '如 deepseek-chat',
  `is_default`  TINYINT(1)   NOT NULL DEFAULT 0,
  `created_at`  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_llm_user` (`user_id`),
  CONSTRAINT `fk_llm_user` FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='大模型配置';

-- ------------------------------------------------------------
-- AI 对话记录
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `chat_messages` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id`    BIGINT UNSIGNED NOT NULL,
  `role`       VARCHAR(16) NOT NULL COMMENT 'user/assistant/system',
  `content`    MEDIUMTEXT  NOT NULL,
  `model_name` VARCHAR(128) NULL,
  `created_at` DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `ix_chat_user` (`user_id`, `id`),
  CONSTRAINT `fk_chat_user` FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='AI 对话记录';
