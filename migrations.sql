CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226160406_InitialCreate') THEN
    CREATE TABLE cameras (
        camera_id character varying(128) NOT NULL,
        ip_address character varying(64) NOT NULL,
        rtsp_username character varying(128) NOT NULL,
        rtsp_password_encrypted text NOT NULL,
        rtsp_profile character varying(64) NOT NULL,
        rtsp_path character varying(256) NOT NULL,
        enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_cameras" PRIMARY KEY (camera_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226160406_InitialCreate') THEN
    CREATE TABLE dlq (
        id uuid NOT NULL,
        subscriber_id uuid NOT NULL,
        endpoint_url character varying(512) NOT NULL,
        idempotency_key character varying(256) NOT NULL,
        payload_json text NOT NULL,
        error text NOT NULL,
        attempt_count integer NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_dlq" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226160406_InitialCreate') THEN
    CREATE TABLE runtime_state (
        key character varying(128) NOT NULL,
        value text NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_runtime_state" PRIMARY KEY (key)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226160406_InitialCreate') THEN
    CREATE TABLE subscribers (
        id uuid NOT NULL,
        name character varying(128) NOT NULL,
        endpoint_url character varying(512) NOT NULL,
        enabled boolean NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_subscribers" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20251226160406_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20251226160406_InitialCreate', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE TABLE face_templates (
        "Id" uuid NOT NULL,
        "PersonId" uuid NOT NULL,
        "FeatureBytes" bytea NOT NULL,
        "L2Norm" real NOT NULL,
        "FeatureVersion" character varying(50) NOT NULL,
        "FaceImageJpeg" bytea,
        "SourceCameraId" character varying(100),
        "FeatureHash" character varying(128) NOT NULL,
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_face_templates" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE TABLE persons (
        "Id" uuid NOT NULL,
        "Code" character varying(100) NOT NULL,
        "FirstName" character varying(200) NOT NULL,
        "LastName" character varying(200) NOT NULL,
        "Gender" character varying(50),
        "Age" integer,
        "Remarks" character varying(500),
        "Category" character varying(100),
        "IsActive" boolean NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_persons" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_face_templates_FeatureHash" ON face_templates ("FeatureHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_face_templates_IsActive" ON face_templates ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_face_templates_PersonId" ON face_templates ("PersonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_face_templates_UpdatedAt" ON face_templates ("UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE UNIQUE INDEX "IX_persons_Code" ON persons ("Code");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_persons_IsActive" ON persons ("IsActive");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    CREATE INDEX "IX_persons_UpdatedAt" ON persons ("UpdatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260105040752_AddLocalWatchlist') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260105040752_AddLocalWatchlist', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE TABLE face_events (
        "Id" uuid NOT NULL,
        "EventTimeUtc" timestamp with time zone NOT NULL,
        "CameraId" character varying(128) NOT NULL,
        "IsKnown" boolean NOT NULL,
        "WatchlistEntryId" character varying(128),
        "PersonId" character varying(256),
        "PersonJson" jsonb,
        "Similarity" real,
        "Score" real,
        "BestshotPath" character varying(1024),
        "ThumbPath" character varying(1024),
        "Gender" character varying(50),
        "Age" integer,
        "Mask" character varying(50),
        "BBoxJson" jsonb,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_face_events" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE INDEX "IX_face_events_CameraId" ON face_events ("CameraId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE INDEX "IX_face_events_EventTimeUtc" ON face_events ("EventTimeUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE INDEX "IX_face_events_IsKnown" ON face_events ("IsKnown");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE INDEX "IX_face_events_PersonId" ON face_events ("PersonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    CREATE INDEX "IX_face_events_WatchlistEntryId" ON face_events ("WatchlistEntryId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260106072850_AddFaceEvents') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260106072850_AddFaceEvents', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260109173909_AddFaceEmbeddingV2') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260109173909_AddFaceEmbeddingV2', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110082530_AddMapLayouts') THEN
    CREATE TABLE map_camera_positions (
        map_id uuid NOT NULL,
        camera_id character varying(128) NOT NULL,
        label character varying(200),
        x real,
        y real,
        latitude double precision,
        longitude double precision,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_map_camera_positions" PRIMARY KEY (map_id, camera_id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110082530_AddMapLayouts') THEN
    CREATE TABLE map_layouts (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        type character varying(16) NOT NULL,
        image_path character varying(512),
        image_width integer,
        image_height integer,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT "PK_map_layouts" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110082530_AddMapLayouts') THEN
    CREATE INDEX "IX_map_camera_positions_map_id" ON map_camera_positions (map_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110082530_AddMapLayouts') THEN
    CREATE INDEX "IX_map_layouts_type" ON map_layouts (type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110082530_AddMapLayouts') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260110082530_AddMapLayouts', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110095308_AddCameraCode') THEN
    ALTER TABLE cameras ADD camera_code character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110095308_AddCameraCode') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260110095308_AddCameraCode', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110102215_AddMapCameraViewSettings') THEN
    ALTER TABLE map_camera_positions ADD angle_degrees real;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110102215_AddMapCameraViewSettings') THEN
    ALTER TABLE map_camera_positions ADD range_value real;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110102215_AddMapCameraViewSettings') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260110102215_AddMapCameraViewSettings', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110142020_AddMapHierarchyAndCameraIconScale') THEN
    ALTER TABLE map_layouts ADD parent_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110142020_AddMapHierarchyAndCameraIconScale') THEN
    ALTER TABLE map_camera_positions ADD icon_scale real;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110142020_AddMapHierarchyAndCameraIconScale') THEN
    CREATE INDEX "IX_map_layouts_parent_id" ON map_layouts (parent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260110142020_AddMapHierarchyAndCameraIconScale') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260110142020_AddMapHierarchyAndCameraIconScale', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111100000_AddMapCameraFovDegrees') THEN
    ALTER TABLE map_camera_positions ADD fov_degrees real;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260111100000_AddMapCameraFovDegrees') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260111100000_AddMapCameraFovDegrees', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260211090000_AddPersonListType') THEN
    ALTER TABLE persons ADD "ListType" character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260211090000_AddPersonListType') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260211090000_AddPersonListType', '8.0.8');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260212090000_AddCameraSeriesModel') THEN
    ALTER TABLE cameras ADD camera_model character varying(128);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260212090000_AddCameraSeriesModel') THEN
    ALTER TABLE cameras ADD camera_series character varying(32);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260212090000_AddCameraSeriesModel') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260212090000_AddCameraSeriesModel', '8.0.8');
    END IF;
END $EF$;
COMMIT;

