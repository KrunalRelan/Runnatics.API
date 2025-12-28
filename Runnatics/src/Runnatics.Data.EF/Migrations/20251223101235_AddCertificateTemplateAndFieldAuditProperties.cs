using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runnatics.Data.EF.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateTemplateAndFieldAuditProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationName = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    EPC = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Available"),
                    BatteryLevel = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chips_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventOrganizer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventOrganizer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventOrganizer_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReaderDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    MacAddress = table.Column<string>(type: "nvarchar(17)", maxLength: 17, nullable: true),
                    FirmwareVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Offline"),
                    LastHeartbeat = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PowerLevel = table.Column<int>(type: "int", nullable: true),
                    AntennaCount = table.Column<int>(type: "int", nullable: false, defaultValue: 4),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderDevices_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    EventOrganizerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EventDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "Asia/Kolkata"),
                    VenueName = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: true),
                    VenueAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VenueLatitude = table.Column<decimal>(type: "decimal(10,8)", nullable: true),
                    VenueLongitude = table.Column<decimal>(type: "decimal(11,8)", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "Draft"),
                    MaxParticipants = table.Column<int>(type: "int", nullable: true),
                    RegistrationDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_EventOrganizer_EventOrganizerId",
                        column: x => x.EventOrganizerId,
                        principalTable: "EventOrganizer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Events_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInvitation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    InvitedBy = table.Column<int>(type: "int", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    IsExpired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedBy = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInvitation_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserInvitation_Users_AcceptedBy",
                        column: x => x.AcceptedBy,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserInvitation_Users_InvitedBy",
                        column: x => x.InvitedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserAgent = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Checkpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DistanceFromStart = table.Column<decimal>(type: "decimal(6,3)", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    ParentDeviceId = table.Column<int>(type: "int", nullable: true),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checkpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Checkpoints_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Checkpoints_Devices_ParentDeviceId",
                        column: x => x.ParentDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Checkpoints_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RemoveBanner = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Published = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RankOnNet = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ShowResultSummaryForRaces = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UseOldData = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ConfirmedEvent = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowNameCheck = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowParticipantEdit = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSettings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorLog = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Distance = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    MaxParticipants = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Races_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadRaws",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    ReaderDeviceId = table.Column<int>(type: "int", nullable: false),
                    ChipEPC = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rssi = table.Column<int>(type: "int", nullable: true),
                    AntennaPort = table.Column<int>(type: "int", nullable: true),
                    IsProcessed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadRaws", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadRaws_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadRaws_ReaderDevices_ReaderDeviceId",
                        column: x => x.ReaderDeviceId,
                        principalTable: "ReaderDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReaderAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    CheckpointId = table.Column<int>(type: "int", nullable: false),
                    ReaderDeviceId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UnassignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReaderAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReaderAssignments_Checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "Checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReaderAssignments_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReaderAssignments_ReaderDevices_ReaderDeviceId",
                        column: x => x.ReaderDeviceId,
                        principalTable: "ReaderDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReaderAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CertificateTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BackgroundImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false, defaultValue: 1754),
                    Height = table.Column<int>(type: "int", nullable: false, defaultValue: 1240),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateTemplates_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateTemplates_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaderboardSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: true),
                    RaceId = table.Column<int>(type: "int", nullable: true),
                    ShowOverallResults = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowCategoryResults = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowGenderResults = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowAgeGroupResults = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    SortByOverallChipTime = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    SortByOverallGunTime = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    SortByCategoryChipTime = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    SortByCategoryGunTime = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    EnableLiveLeaderboard = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowSplitTimes = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowPace = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    ShowTeamResults = table.Column<bool>(type: "bit", nullable: true, defaultValue: false),
                    ShowMedalIcon = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    AllowAnonymousView = table.Column<bool>(type: "bit", nullable: true, defaultValue: true),
                    AutoRefreshIntervalSec = table.Column<int>(type: "int", nullable: true, defaultValue: 30),
                    MaxDisplayedRecords = table.Column<int>(type: "int", nullable: true, defaultValue: 100),
                    NumberOfResultsToShowOverall = table.Column<int>(type: "int", nullable: true),
                    NumberOfResultsToShowCategory = table.Column<int>(type: "int", nullable: true),
                    OverrideSettings = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaderboardSettings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeaderboardSettings_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    ImportBatchId = table.Column<int>(type: "int", nullable: true),
                    Bib = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "date", nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AgeCategory = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    State = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmergencyContact = table.Column<string>(type: "nvarchar(510)", maxLength: 510, nullable: true),
                    EmergencyPhone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TShirtSize = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    RegistrationStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Participants_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Participants_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Participants_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RaceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    Published = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    SendSms = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CheckValidation = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ShowLeaderboard = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ShowResultTable = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsTimed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PublishDNF = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DedUpSeconds = table.Column<int>(type: "int", nullable: true),
                    EarlyStartCutOff = table.Column<int>(type: "int", nullable: true),
                    LateStartCutOff = table.Column<int>(type: "int", nullable: true),
                    HasLoops = table.Column<bool>(type: "bit", nullable: true),
                    LoopLength = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DataHeader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaceSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaceSettings_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CertificateFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    FieldType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    XCoordinate = table.Column<int>(type: "int", nullable: false),
                    YCoordinate = table.Column<int>(type: "int", nullable: false),
                    Font = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "Arial"),
                    FontSize = table.Column<int>(type: "int", nullable: false, defaultValue: 12),
                    FontColor = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false, defaultValue: "000000"),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Alignment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "left"),
                    FontWeight = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "normal"),
                    FontStyle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "normal"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateFields_CertificateTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "CertificateTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChipAssignments",
                columns: table => new
                {
                    EventId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    ChipId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UnassignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChipAssignments", x => new { x.EventId, x.ParticipantId, x.ChipId });
                    table.ForeignKey(
                        name: "FK_ChipAssignments_Chips_ChipId",
                        column: x => x.ChipId,
                        principalTable: "Chips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChipAssignments_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChipAssignments_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChipAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: true),
                    ParticipantId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    ParticipantId1 = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Participants_ParticipantId1",
                        column: x => x.ParticipantId1,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ParticipantStaging",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Bib = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AgeCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Mobile = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProcessingStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParticipantId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantStaging", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipantStaging_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParticipantStaging_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReadNormalized",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    CheckpointId = table.Column<int>(type: "int", nullable: false),
                    RawReadId = table.Column<long>(type: "bigint", nullable: true),
                    ChipTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GunTime = table.Column<long>(type: "bigint", nullable: true),
                    NetTime = table.Column<long>(type: "bigint", nullable: true),
                    IsManualEntry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ManualEntryReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EventId1 = table.Column<int>(type: "int", nullable: true),
                    ParticipantId1 = table.Column<int>(type: "int", nullable: true),
                    ReadRawId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadNormalized", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "Checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Events_EventId1",
                        column: x => x.EventId1,
                        principalTable: "Events",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Participants_ParticipantId1",
                        column: x => x.ParticipantId1,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadNormalized_ReadRaws_RawReadId",
                        column: x => x.RawReadId,
                        principalTable: "ReadRaws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReadNormalized_ReadRaws_ReadRawId",
                        column: x => x.ReadRawId,
                        principalTable: "ReadRaws",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReadNormalized_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    FinishTime = table.Column<long>(type: "bigint", nullable: true),
                    GunTime = table.Column<long>(type: "bigint", nullable: true),
                    NetTime = table.Column<long>(type: "bigint", nullable: true),
                    OverallRank = table.Column<int>(type: "int", nullable: true),
                    GenderRank = table.Column<int>(type: "int", nullable: true),
                    CategoryRank = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Finished"),
                    DisqualificationReason = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IsOfficial = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CertificateGenerated = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Results_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Results_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Results_Races_RaceId",
                        column: x => x.RaceId,
                        principalTable: "Races",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SplitTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    ParticipantId = table.Column<int>(type: "int", nullable: false),
                    CheckpointId = table.Column<int>(type: "int", nullable: false),
                    ReadNormalizedId = table.Column<int>(type: "int", nullable: true),
                    SplitTimeMs = table.Column<long>(type: "bigint", nullable: false),
                    SegmentTime = table.Column<long>(type: "bigint", nullable: true),
                    Pace = table.Column<decimal>(type: "decimal(10,3)", nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    GenderRank = table.Column<int>(type: "int", nullable: true),
                    CategoryRank = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AuditProperties_CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    AuditProperties_CreatedBy = table.Column<int>(type: "int", nullable: false),
                    AuditProperties_UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    AuditProperties_UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuditProperties_IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    EventId1 = table.Column<int>(type: "int", nullable: true),
                    ParticipantId1 = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitTimes_Checkpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "Checkpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SplitTimes_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SplitTimes_Events_EventId1",
                        column: x => x.EventId1,
                        principalTable: "Events",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SplitTimes_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SplitTimes_Participants_ParticipantId1",
                        column: x => x.ParticipantId1,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SplitTimes_ReadNormalized_ReadNormalizedId",
                        column: x => x.ReadNormalizedId,
                        principalTable: "ReadNormalized",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateFields_TemplateId",
                table: "CertificateFields",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateTemplates_EventId",
                table: "CertificateTemplates",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateTemplates_EventId_RaceId",
                table: "CertificateTemplates",
                columns: new[] { "EventId", "RaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateTemplates_RaceId",
                table: "CertificateTemplates",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_DeviceId",
                table: "Checkpoints",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_EventId",
                table: "Checkpoints",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_EventId_DistanceKm",
                table: "Checkpoints",
                columns: new[] { "EventId", "DistanceFromStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Checkpoints_ParentDeviceId",
                table: "Checkpoints",
                column: "ParentDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ChipAssignments_AssignedAt",
                table: "ChipAssignments",
                column: "AssignedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChipAssignments_AssignedByUserId",
                table: "ChipAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChipAssignments_ChipId",
                table: "ChipAssignments",
                column: "ChipId");

            migrationBuilder.CreateIndex(
                name: "IX_ChipAssignments_EventId",
                table: "ChipAssignments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ChipAssignments_ParticipantId",
                table: "ChipAssignments",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Chips_EPC",
                table: "Chips",
                column: "EPC",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chips_TenantId_Status",
                table: "Chips",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventOrganizer_TenantId",
                table: "EventOrganizer",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventDate",
                table: "Events",
                column: "EventDate");

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventOrganizerId",
                table: "Events",
                column: "EventOrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_Slug",
                table: "Events",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_TenantId_Status",
                table: "Events",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventSettings_EventId",
                table: "EventSettings",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_EventId",
                table: "ImportBatches",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status",
                table: "ImportBatches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId",
                table: "ImportBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSettings_EventId",
                table: "LeaderboardSettings",
                column: "EventId",
                unique: true,
                filter: "[EventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSettings_RaceId",
                table: "LeaderboardSettings",
                column: "RaceId",
                unique: true,
                filter: "[RaceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId",
                table: "Notifications",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ParticipantId",
                table: "Notifications",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ParticipantId1",
                table: "Notifications",
                column: "ParticipantId1");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SentAt",
                table: "Notifications",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Status",
                table: "Notifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Type",
                table: "Notifications",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Domain",
                table: "Organizations",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Participants_EventId_BibNumber",
                table: "Participants",
                columns: new[] { "EventId", "Bib" },
                unique: true,
                filter: "[Bib] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_ImportBatchId",
                table: "Participants",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_RaceId",
                table: "Participants",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Participants_TenantId_Email",
                table: "Participants",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantStaging_ImportBatchId",
                table: "ParticipantStaging",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantStaging_ParticipantId",
                table: "ParticipantStaging",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantStaging_ProcessingStatus",
                table: "ParticipantStaging",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_ExpiresAt",
                table: "PasswordResetTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Races_EventId",
                table: "Races",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_RaceSettings_RaceId",
                table: "RaceSettings",
                column: "RaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderAssignments_AssignedAt",
                table: "ReaderAssignments",
                column: "AssignedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderAssignments_AssignedByUserId",
                table: "ReaderAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderAssignments_CheckpointId",
                table: "ReaderAssignments",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderAssignments_EventId",
                table: "ReaderAssignments",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderAssignments_ReaderDeviceId",
                table: "ReaderAssignments",
                column: "ReaderDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderDevices_MacAddress",
                table: "ReaderDevices",
                column: "MacAddress");

            migrationBuilder.CreateIndex(
                name: "IX_ReaderDevices_SerialNumber",
                table: "ReaderDevices",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReaderDevices_TenantId",
                table: "ReaderDevices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_CheckpointId",
                table: "ReadNormalized",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_ChipTime",
                table: "ReadNormalized",
                column: "ChipTime");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_CreatedByUserId",
                table: "ReadNormalized",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_EventId",
                table: "ReadNormalized",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_EventId1",
                table: "ReadNormalized",
                column: "EventId1");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_GunTime",
                table: "ReadNormalized",
                column: "GunTime");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_ParticipantId",
                table: "ReadNormalized",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_ParticipantId1",
                table: "ReadNormalized",
                column: "ParticipantId1");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_RawReadId",
                table: "ReadNormalized",
                column: "RawReadId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadNormalized_ReadRawId",
                table: "ReadNormalized",
                column: "ReadRawId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadRaws_ChipEPC_Timestamp",
                table: "ReadRaws",
                columns: new[] { "ChipEPC", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadRaws_EventId_Timestamp",
                table: "ReadRaws",
                columns: new[] { "EventId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadRaws_IsProcessed",
                table: "ReadRaws",
                column: "IsProcessed",
                filter: "[IsProcessed] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReadRaws_ReaderDeviceId",
                table: "ReadRaws",
                column: "ReaderDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_EventId_CategoryRank",
                table: "Results",
                columns: new[] { "EventId", "CategoryRank" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_EventId_GenderRank",
                table: "Results",
                columns: new[] { "EventId", "GenderRank" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_EventId_OverallRank",
                table: "Results",
                columns: new[] { "EventId", "OverallRank" });

            migrationBuilder.CreateIndex(
                name: "IX_Results_ParticipantId",
                table: "Results",
                column: "ParticipantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Results_RaceId",
                table: "Results",
                column: "RaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_CheckpointId",
                table: "SplitTimes",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_EventId",
                table: "SplitTimes",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_EventId1",
                table: "SplitTimes",
                column: "EventId1");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_ParticipantId",
                table: "SplitTimes",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_ParticipantId1",
                table: "SplitTimes",
                column: "ParticipantId1");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_Rank",
                table: "SplitTimes",
                column: "Rank");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_ReadNormalizedId",
                table: "SplitTimes",
                column: "ReadNormalizedId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitTimes_SplitTimeMs",
                table: "SplitTimes",
                column: "SplitTimeMs");

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitation_AcceptedBy",
                table: "UserInvitation",
                column: "AcceptedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitation_InvitedBy",
                table: "UserInvitation",
                column: "InvitedBy");

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitation_TenantId",
                table: "UserInvitation",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_ExpiresAt",
                table: "UserSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TokenHash",
                table: "UserSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateFields");

            migrationBuilder.DropTable(
                name: "ChipAssignments");

            migrationBuilder.DropTable(
                name: "EventSettings");

            migrationBuilder.DropTable(
                name: "LeaderboardSettings");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ParticipantStaging");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "RaceSettings");

            migrationBuilder.DropTable(
                name: "ReaderAssignments");

            migrationBuilder.DropTable(
                name: "Results");

            migrationBuilder.DropTable(
                name: "SplitTimes");

            migrationBuilder.DropTable(
                name: "UserInvitation");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "CertificateTemplates");

            migrationBuilder.DropTable(
                name: "Chips");

            migrationBuilder.DropTable(
                name: "ReadNormalized");

            migrationBuilder.DropTable(
                name: "Checkpoints");

            migrationBuilder.DropTable(
                name: "Participants");

            migrationBuilder.DropTable(
                name: "ReadRaws");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropTable(
                name: "ReaderDevices");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "EventOrganizer");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
