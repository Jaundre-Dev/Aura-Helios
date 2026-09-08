CREATE TABLE IF NOT EXISTS `__helios_migrations_history` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___helios_migrations_history` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `audit_logs` (
        `id` binary(16) NOT NULL,
        `occurred_at` datetime(6) NOT NULL,
        `actor_user_id` binary(16) NULL,
        `agent_run_id` binary(16) NULL,
        `workspace_id` binary(16) NULL,
        `action` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `resource_type` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `resource_id` varchar(100) CHARACTER SET utf8mb4 NULL,
        `allowed` tinyint(1) NOT NULL,
        `deny_reason` varchar(500) CHARACTER SET utf8mb4 NULL,
        `metadata` json NULL,
        `ip_address` varchar(45) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_audit_logs` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `organizations` (
        `id` binary(16) NOT NULL,
        `name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `is_active` tinyint(1) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_organizations` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `projects` (
        `id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `description` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `classification` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `is_active` tinyint(1) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_projects` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `roles` (
        `id` binary(16) NOT NULL,
        `name` varchar(256) CHARACTER SET utf8mb4 NULL,
        `normalized_name` varchar(256) CHARACTER SET utf8mb4 NULL,
        `concurrency_stamp` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_roles` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `users` (
        `id` binary(16) NOT NULL,
        `display_name` longtext CHARACTER SET utf8mb4 NULL,
        `created_at` datetime(6) NOT NULL,
        `last_sign_in_at` datetime(6) NULL,
        `is_active` tinyint(1) NOT NULL,
        `default_workspace_id` binary(16) NULL,
        `user_name` varchar(256) CHARACTER SET utf8mb4 NULL,
        `normalized_user_name` varchar(256) CHARACTER SET utf8mb4 NULL,
        `email` varchar(256) CHARACTER SET utf8mb4 NULL,
        `normalized_email` varchar(256) CHARACTER SET utf8mb4 NULL,
        `email_confirmed` tinyint(1) NOT NULL,
        `password_hash` longtext CHARACTER SET utf8mb4 NULL,
        `security_stamp` longtext CHARACTER SET utf8mb4 NULL,
        `concurrency_stamp` longtext CHARACTER SET utf8mb4 NULL,
        `phone_number` longtext CHARACTER SET utf8mb4 NULL,
        `phone_number_confirmed` tinyint(1) NOT NULL,
        `two_factor_enabled` tinyint(1) NOT NULL,
        `lockout_end` datetime(6) NULL,
        `lockout_enabled` tinyint(1) NOT NULL,
        `access_failed_count` int NOT NULL,
        CONSTRAINT `pk_users` PRIMARY KEY (`id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `workspaces` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `description` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `is_active` tinyint(1) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_workspaces` PRIMARY KEY (`id`),
        CONSTRAINT `fk_workspaces_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `project_members` (
        `id` binary(16) NOT NULL,
        `project_id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `user_id` binary(16) NOT NULL,
        `role` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_project_members` PRIMARY KEY (`id`),
        CONSTRAINT `fk_project_members_projects_project_id` FOREIGN KEY (`project_id`) REFERENCES `projects` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `role_claims` (
        `id` int NOT NULL AUTO_INCREMENT,
        `role_id` binary(16) NOT NULL,
        `claim_type` longtext CHARACTER SET utf8mb4 NULL,
        `claim_value` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_role_claims` PRIMARY KEY (`id`),
        CONSTRAINT `fk_role_claims_roles_role_id` FOREIGN KEY (`role_id`) REFERENCES `roles` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `user_claims` (
        `id` int NOT NULL AUTO_INCREMENT,
        `user_id` binary(16) NOT NULL,
        `claim_type` longtext CHARACTER SET utf8mb4 NULL,
        `claim_value` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_user_claims` PRIMARY KEY (`id`),
        CONSTRAINT `fk_user_claims_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `user_logins` (
        `login_provider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `provider_key` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `provider_display_name` longtext CHARACTER SET utf8mb4 NULL,
        `user_id` binary(16) NOT NULL,
        CONSTRAINT `pk_user_logins` PRIMARY KEY (`login_provider`, `provider_key`),
        CONSTRAINT `fk_user_logins_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `user_roles` (
        `user_id` binary(16) NOT NULL,
        `role_id` binary(16) NOT NULL,
        CONSTRAINT `pk_user_roles` PRIMARY KEY (`user_id`, `role_id`),
        CONSTRAINT `fk_user_roles_roles_role_id` FOREIGN KEY (`role_id`) REFERENCES `roles` (`id`) ON DELETE CASCADE,
        CONSTRAINT `fk_user_roles_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `user_tokens` (
        `user_id` binary(16) NOT NULL,
        `login_provider` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `name` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `value` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_user_tokens` PRIMARY KEY (`user_id`, `login_provider`, `name`),
        CONSTRAINT `fk_user_tokens_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE TABLE `workspace_members` (
        `id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `user_id` binary(16) NOT NULL,
        `role` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_workspace_members` PRIMARY KEY (`id`),
        CONSTRAINT `fk_workspace_members_workspaces_workspace_id` FOREIGN KEY (`workspace_id`) REFERENCES `workspaces` (`id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_audit_logs_agent_run_id` ON `audit_logs` (`agent_run_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_audit_logs_allowed_occurred_at` ON `audit_logs` (`allowed`, `occurred_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_audit_logs_occurred_at` ON `audit_logs` (`occurred_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_audit_logs_workspace_id_occurred_at` ON `audit_logs` (`workspace_id`, `occurred_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `ix_organizations_slug` ON `organizations` (`slug`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `ix_project_members_project_id_user_id` ON `project_members` (`project_id`, `user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_project_members_user_id` ON `project_members` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_project_members_workspace_id` ON `project_members` (`workspace_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `ix_projects_workspace_id_slug` ON `projects` (`workspace_id`, `slug`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_role_claims_role_id` ON `role_claims` (`role_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `role_name_index` ON `roles` (`normalized_name`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_user_claims_user_id` ON `user_claims` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_user_logins_user_id` ON `user_logins` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_user_roles_role_id` ON `user_roles` (`role_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `email_index` ON `users` (`normalized_email`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `user_name_index` ON `users` (`normalized_user_name`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE INDEX `ix_workspace_members_user_id` ON `workspace_members` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `ix_workspace_members_workspace_id_user_id` ON `workspace_members` (`workspace_id`, `user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    CREATE UNIQUE INDEX `ix_workspaces_organization_id_slug` ON `workspaces` (`organization_id`, `slug`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260907165743_Initial') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20260907165743_Initial', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

