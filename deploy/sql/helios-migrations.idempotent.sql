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

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921183647_SecretStore') THEN

    CREATE TABLE `secrets` (
        `id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `reference` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `envelope` varbinary(1052) NOT NULL,
        `key_id` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_secrets` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921183647_SecretStore') THEN

    CREATE UNIQUE INDEX `ix_secrets_workspace_id_reference` ON `secrets` (`workspace_id`, `reference`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921183647_SecretStore') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20260921183647_SecretStore', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921184506_StoredObjects') THEN

    CREATE TABLE `stored_objects` (
        `id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `name` varchar(400) CHARACTER SET utf8mb4 NOT NULL,
        `content_type` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `content` longblob NOT NULL,
        `size_bytes` bigint NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_stored_objects` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921184506_StoredObjects') THEN

    CREATE INDEX `ix_stored_objects_workspace_id` ON `stored_objects` (`workspace_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20260921184506_StoredObjects') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20260921184506_StoredObjects', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    ALTER TABLE `audit_logs` ADD `correlation_id` varchar(64) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    ALTER TABLE `audit_logs` ADD `organization_id` binary(16) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    CREATE TABLE `organization_members` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `user_id` binary(16) NOT NULL,
        `role` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `is_active` tinyint(1) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_organization_members` PRIMARY KEY (`id`),
        CONSTRAINT `fk_organization_members_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    CREATE INDEX `ix_audit_logs_correlation_id` ON `audit_logs` (`correlation_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    CREATE INDEX `ix_audit_logs_organization_id_occurred_at` ON `audit_logs` (`organization_id`, `occurred_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    CREATE UNIQUE INDEX `ix_organization_members_organization_id_user_id` ON `organization_members` (`organization_id`, `user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    CREATE INDEX `ix_organization_members_user_id` ON `organization_members` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    INSERT INTO organization_members (id, organization_id, user_id, role, is_active, created_at, created_by)
    SELECT UUID_TO_BIN(UUID(), 1), o.id, o.created_by, 'Owner', 1, UTC_TIMESTAMP(6), NULL
    FROM organizations AS o
    WHERE o.created_by IS NOT NULL
      AND EXISTS (SELECT 1 FROM users AS u WHERE u.id = o.created_by);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    INSERT INTO organization_members (id, organization_id, user_id, role, is_active, created_at, created_by)
    SELECT UUID_TO_BIN(UUID(), 1), x.organization_id, x.user_id,
           CASE WHEN x.is_admin = 1 THEN 'Admin' ELSE 'Operator' END,
           1, UTC_TIMESTAMP(6), NULL
    FROM (
        SELECT w.organization_id, wm.user_id, MAX(wm.role IN ('Owner', 'Admin')) AS is_admin
        FROM workspace_members AS wm
        INNER JOIN workspaces AS w ON w.id = wm.workspace_id
        GROUP BY w.organization_id, wm.user_id
    ) AS x
    WHERE NOT EXISTS (
        SELECT 1 FROM organization_members AS m
        WHERE m.organization_id = x.organization_id AND m.user_id = x.user_id);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003094154_OrganizationMembershipAndAuditCorrelation') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20261003094154_OrganizationMembershipAndAuditCorrelation', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `api_keys` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `project_id` binary(16) NULL,
        `name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `public_id` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
        `display_prefix` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
        `secret_hash` varbinary(32) NOT NULL,
        `environment` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `scopes` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
        `expires_at` datetime(6) NULL,
        `revoked_at` datetime(6) NULL,
        `revoked_by` binary(16) NULL,
        `last_used_at` datetime(6) NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_api_keys` PRIMARY KEY (`id`),
        CONSTRAINT `fk_api_keys_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_api_keys_workspaces_workspace_id` FOREIGN KEY (`workspace_id`) REFERENCES `workspaces` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `api_products` (
        `id` binary(16) NOT NULL,
        `slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `category` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `summary` varchar(1000) CHARACTER SET utf8mb4 NOT NULL,
        `delivery` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `release_state` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `sensitivity` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
        `billing_unit` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `current_version` varchar(20) CHARACTER SET utf8mb4 NULL,
        `limitations` varchar(2000) CHARACTER SET utf8mb4 NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_api_products` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `billing_profiles` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `legal_name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `billing_email` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
        `address_line1` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `address_line2` varchar(200) CHARACTER SET utf8mb4 NULL,
        `city` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `province` varchar(100) CHARACTER SET utf8mb4 NULL,
        `postal_code` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `country_code` varchar(2) CHARACTER SET utf8mb4 NOT NULL,
        `registration_number` varchar(50) CHARACTER SET utf8mb4 NULL,
        `vat_number` varchar(20) CHARACTER SET utf8mb4 NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_billing_profiles` PRIMARY KEY (`id`),
        CONSTRAINT `fk_billing_profiles_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `api_product_versions` (
        `id` binary(16) NOT NULL,
        `product_id` binary(16) NOT NULL,
        `version` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `release_state` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `max_input_bytes` int NOT NULL,
        `request_schema_json` json NULL,
        `response_schema_json` json NULL,
        `request_example` json NULL,
        `published_at` datetime(6) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_api_product_versions` PRIMARY KEY (`id`),
        CONSTRAINT `fk_api_product_versions_api_products_product_id` FOREIGN KEY (`product_id`) REFERENCES `api_products` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `api_requests` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `product_id` binary(16) NOT NULL,
        `product_slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `product_version` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `environment` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `channel` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `actor_user_id` binary(16) NULL,
        `api_key_id` binary(16) NULL,
        `status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `idempotency_key` varchar(255) CHARACTER SET utf8mb4 NULL,
        `fingerprint_key_id` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `payload_fingerprint` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `result_json` json NULL,
        `warnings_json` json NULL,
        `review_required` tinyint(1) NOT NULL,
        `error_code` varchar(100) CHARACTER SET utf8mb4 NULL,
        `usage_unit` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `usage_quantity` decimal(18,6) NOT NULL,
        `billing_state` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `billing_amount` decimal(18,6) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `completed_at` datetime(6) NULL,
        `result_expires_at` datetime(6) NULL,
        CONSTRAINT `pk_api_requests` PRIMARY KEY (`id`),
        CONSTRAINT `fk_api_requests_api_products_product_id` FOREIGN KEY (`product_id`) REFERENCES `api_products` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_api_requests_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_api_requests_workspaces_workspace_id` FOREIGN KEY (`workspace_id`) REFERENCES `workspaces` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE TABLE `entitlements` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `product_id` binary(16) NOT NULL,
        `environment` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `state` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `purpose` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        `updated_at` datetime(6) NULL,
        `updated_by` binary(16) NULL,
        CONSTRAINT `pk_entitlements` PRIMARY KEY (`id`),
        CONSTRAINT `fk_entitlements_api_products_product_id` FOREIGN KEY (`product_id`) REFERENCES `api_products` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_entitlements_organizations_organization_id` FOREIGN KEY (`organization_id`) REFERENCES `organizations` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_api_keys_organization_id` ON `api_keys` (`organization_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_api_keys_public_id` ON `api_keys` (`public_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_api_keys_workspace_id_created_at` ON `api_keys` (`workspace_id`, `created_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_api_product_versions_product_id_version` ON `api_product_versions` (`product_id`, `version`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_api_products_slug` ON `api_products` (`slug`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_api_requests_api_key_id` ON `api_requests` (`api_key_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_api_requests_organization_id_environment_product_slug_produc~` ON `api_requests` (`organization_id`, `environment`, `product_slug`, `product_version`, `idempotency_key`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_api_requests_product_id` ON `api_requests` (`product_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_api_requests_workspace_id_created_at` ON `api_requests` (`workspace_id`, `created_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_billing_profiles_organization_id` ON `billing_profiles` (`organization_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE UNIQUE INDEX `ix_entitlements_organization_id_product_id_environment` ON `entitlements` (`organization_id`, `product_id`, `environment`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    CREATE INDEX `ix_entitlements_product_id` ON `entitlements` (`product_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000001'), 'ocr.general', 'General OCR', 'Documents', 'Text from scanned documents and photos for admin and software platforms.', 'AI',
         'Planned', 'Personal', 'Page', NULL,
         'Planned: no OCR engine has been selected or evaluated yet.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000002'), 'documents.sa-id', 'SA ID document extraction', 'Documents', 'Fields from South African identity documents for onboarding.', 'Hybrid',
         'Planned', 'Personal', 'Document, stated sides', NULL,
         'Planned. Extraction does not establish that a document is authentic.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000003'), 'documents.invoice', 'Invoice extraction', 'Documents', 'Supplier, totals, VAT and line items for accounts payable.', 'Hybrid',
         'Planned', 'Personal', 'Document + excess pages', NULL,
         'Planned. Arithmetic warnings are checks on extracted values, not an audit.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000004'), 'documents.bank-statement', 'Bank statement extraction', 'Documents', 'Structured transactions and balances for accounting, property and finance workflows.', 'Hybrid',
         'Planned', 'Personal', 'Document + excess pages', NULL,
         'Planned. Statement arithmetic checks are not affordability, credit or fraud decisions.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000005'), 'documents.payslip', 'Payslip extraction', 'Documents', 'Pay, employer and deduction capture for property and HR workflows.', 'Hybrid',
         'Planned', 'Personal', 'Document', NULL,
         'Planned. Extraction does not verify employment or income.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000006'), 'documents.proof-of-address', 'Proof-of-address extraction', 'Documents', 'Name, address, issuer and date capture for onboarding.', 'Hybrid',
         'Planned', 'Personal', 'Document', NULL,
         'Planned. Extraction is not authoritative address verification.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000007'), 'documents.classify', 'Document classification', 'Documents', 'Document type detection and routing for document workflows.', 'AI',
         'Planned', 'Personal', 'Document/page band', NULL,
         'Planned: no classification model has been evaluated yet.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000008'), 'identity.sa-id-validate', 'SA ID number validation', 'Identity', 'Format, date-of-birth and checksum validation of South African ID numbers to catch input mistakes.', 'Build',
         'Sandbox', 'Personal', 'Request', '1',
         'Structural validation only. Does not confirm the number was issued, belongs to the applicant or is current; not a Home Affairs verification.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-000000000009'), 'identity.face-compare', 'Face comparison', 'Identity', 'Selfie-to-document comparison for onboarding through a specialist provider.', 'Partner',
         'Planned', 'SpecialPersonal', 'Comparison', NULL,
         'Unavailable: requires an approved specialist provider contract, validated thresholds and POPIA biometric review.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-00000000000a'), 'identity.liveness-passive', 'Passive liveness', 'Identity', 'Specialist presentation-attack detection for onboarding.', 'Partner',
         'Planned', 'SpecialPersonal', 'Session with attempt allowance', NULL,
         'Unavailable: requires an approved specialist provider and capture SDK. Never a generic AI judgement.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-00000000000b'), 'banking.account-verify', 'Bank account verification', 'Company and financial', 'Account and holder match to reduce payment errors.', 'Partner',
         'Planned', 'Personal', 'Completed check', NULL,
         'Unavailable: requires an authorised verification partner contract.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_products
        (id, slug, name, category, summary, delivery, release_state, sensitivity, billing_unit,
         current_version, limitations, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0001-7000-8000-00000000000c'), 'company.lookup', 'Company registration lookup', 'Company and financial', 'Registration lookup for supplier and merchant onboarding.', 'Partner',
         'Planned', 'Standard', 'Lookup', NULL,
         'Unavailable: requires authorised registry access with redistribution permission.', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO api_product_versions
        (id, product_id, version, release_state, max_input_bytes, request_schema_json, response_schema_json,
         request_example, published_at, created_at, created_by, updated_at, updated_by)
    VALUES
        (UUID_TO_BIN('01999a3c-0002-7000-8000-000000000008'), UUID_TO_BIN('01999a3c-0001-7000-8000-000000000008'),
         '1', 'Sandbox', 1024, '{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","additionalProperties":false,"required":["idNumber"],"properties":{"idNumber":{"type":"string","maxLength":32,"description":"13-digit South African ID number. Spaces are ignored."}}}', '{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","required":["valid","checks","derived","notice"],"properties":{"valid":{"type":"boolean"},"checks":{"type":"object","properties":{"format":{"enum":["pass","fail","not_evaluated"]},"dateOfBirth":{"enum":["pass","fail","not_evaluated"]},"citizenship":{"enum":["pass","fail","not_evaluated"]},"checksum":{"enum":["pass","fail","not_evaluated"]}}},"derived":{"type":"object","properties":{"dateOfBirth":{"type":["string","null"],"format":"date"},"citizenship":{"enum":["citizen","permanent_resident","refugee",null]}}},"notice":{"type":"string"}}}', '{"idNumber":"8001015009087"}',
         '2026-10-03 00:00:00.000000', '2026-10-03 00:00:00.000000', NULL, NULL, NULL);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003100142_CatalogueKeysAndRequests') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20261003100142_CatalogueKeysAndRequests', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    ALTER TABLE `api_requests` ADD `price_version_id` binary(16) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    ALTER TABLE `api_requests` ADD `reserved_amount` decimal(19,6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `jobs` (
        `id` binary(16) NOT NULL,
        `api_request_id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `product_slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `product_version` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `phase` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `attempts` int NOT NULL,
        `max_attempts` int NOT NULL,
        `reconcile_attempts` int NOT NULL,
        `available_at` datetime(6) NOT NULL,
        `lease_owner` varchar(200) CHARACTER SET utf8mb4 NULL,
        `lease_expires_at` datetime(6) NULL,
        `fencing_token` bigint NOT NULL,
        `execution_started_at` datetime(6) NULL,
        `input_envelope` mediumblob NULL,
        `input_key_id` varchar(64) CHARACTER SET utf8mb4 NULL,
        `provider_reference` varchar(200) CHARACTER SET utf8mb4 NULL,
        `last_error` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `created_at` datetime(6) NOT NULL,
        `completed_at` datetime(6) NULL,
        CONSTRAINT `pk_jobs` PRIMARY KEY (`id`),
        CONSTRAINT `fk_jobs_api_requests_api_request_id` FOREIGN KEY (`api_request_id`) REFERENCES `api_requests` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_jobs_workspaces_workspace_id` FOREIGN KEY (`workspace_id`) REFERENCES `workspaces` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `ledger_accounts` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `type` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `balance` decimal(19,6) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        CONSTRAINT `pk_ledger_accounts` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `ledger_transactions` (
        `id` binary(16) NOT NULL,
        `type` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `posting_key` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
        `organization_id` binary(16) NULL,
        `api_request_id` binary(16) NULL,
        `payment_id` binary(16) NULL,
        `reverses_transaction_id` binary(16) NULL,
        `description` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        CONSTRAINT `pk_ledger_transactions` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `price_versions` (
        `id` binary(16) NOT NULL,
        `product_id` binary(16) NOT NULL,
        `environment` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `unit` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `unit_price` decimal(19,6) NOT NULL,
        `minimum_charge` decimal(19,6) NOT NULL,
        `tax_treatment` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `effective_from` datetime(6) NOT NULL,
        `effective_to` datetime(6) NULL,
        `created_at` datetime(6) NOT NULL,
        `created_by` binary(16) NULL,
        CONSTRAINT `pk_price_versions` PRIMARY KEY (`id`),
        CONSTRAINT `fk_price_versions_api_products_product_id` FOREIGN KEY (`product_id`) REFERENCES `api_products` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `reservations` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `api_request_id` binary(16) NOT NULL,
        `amount` decimal(19,6) NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `state` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `settled_amount` decimal(19,6) NULL,
        `created_at` datetime(6) NOT NULL,
        `resolved_at` datetime(6) NULL,
        CONSTRAINT `pk_reservations` PRIMARY KEY (`id`),
        CONSTRAINT `fk_reservations_api_requests_api_request_id` FOREIGN KEY (`api_request_id`) REFERENCES `api_requests` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `ledger_entries` (
        `id` binary(16) NOT NULL,
        `transaction_id` binary(16) NOT NULL,
        `account_id` binary(16) NOT NULL,
        `amount` decimal(19,6) NOT NULL,
        CONSTRAINT `pk_ledger_entries` PRIMARY KEY (`id`),
        CONSTRAINT `fk_ledger_entries_ledger_accounts_account_id` FOREIGN KEY (`account_id`) REFERENCES `ledger_accounts` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_ledger_entries_ledger_transactions_transaction_id` FOREIGN KEY (`transaction_id`) REFERENCES `ledger_transactions` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE TABLE `usage_events` (
        `id` binary(16) NOT NULL,
        `organization_id` binary(16) NOT NULL,
        `workspace_id` binary(16) NOT NULL,
        `api_request_id` binary(16) NOT NULL,
        `product_id` binary(16) NOT NULL,
        `product_slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `product_version` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `environment` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `price_version_id` binary(16) NULL,
        `unit` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `quantity` decimal(19,6) NOT NULL,
        `amount` decimal(19,6) NOT NULL,
        `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL,
        `occurred_at` datetime(6) NOT NULL,
        CONSTRAINT `pk_usage_events` PRIMARY KEY (`id`),
        CONSTRAINT `fk_usage_events_api_requests_api_request_id` FOREIGN KEY (`api_request_id`) REFERENCES `api_requests` (`id`) ON DELETE RESTRICT,
        CONSTRAINT `fk_usage_events_price_versions_price_version_id` FOREIGN KEY (`price_version_id`) REFERENCES `price_versions` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_api_requests_price_version_id` ON `api_requests` (`price_version_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_jobs_api_request_id` ON `jobs` (`api_request_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_jobs_status_available_at` ON `jobs` (`status`, `available_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_jobs_status_lease_expires_at` ON `jobs` (`status`, `lease_expires_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_jobs_workspace_id` ON `jobs` (`workspace_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_ledger_accounts_organization_id_type_currency` ON `ledger_accounts` (`organization_id`, `type`, `currency`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_ledger_entries_account_id` ON `ledger_entries` (`account_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_ledger_entries_transaction_id` ON `ledger_entries` (`transaction_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_ledger_transactions_api_request_id` ON `ledger_transactions` (`api_request_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_ledger_transactions_organization_id_created_at` ON `ledger_transactions` (`organization_id`, `created_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_ledger_transactions_payment_id` ON `ledger_transactions` (`payment_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_ledger_transactions_posting_key` ON `ledger_transactions` (`posting_key`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_price_versions_product_id_environment_effective_from` ON `price_versions` (`product_id`, `environment`, `effective_from`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_reservations_api_request_id` ON `reservations` (`api_request_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_reservations_organization_id_state` ON `reservations` (`organization_id`, `state`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE UNIQUE INDEX `ix_usage_events_api_request_id` ON `usage_events` (`api_request_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_usage_events_organization_id_occurred_at` ON `usage_events` (`organization_id`, `occurred_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    CREATE INDEX `ix_usage_events_price_version_id` ON `usage_events` (`price_version_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    ALTER TABLE `api_requests` ADD CONSTRAINT `fk_api_requests_price_versions_price_version_id` FOREIGN KEY (`price_version_id`) REFERENCES `price_versions` (`id`) ON DELETE RESTRICT;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__helios_migrations_history` WHERE `MigrationId` = '20261003102644_LedgerPricingAndJobs') THEN

    INSERT INTO `__helios_migrations_history` (`MigrationId`, `ProductVersion`)
    VALUES ('20261003102644_LedgerPricingAndJobs', '9.0.19');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

