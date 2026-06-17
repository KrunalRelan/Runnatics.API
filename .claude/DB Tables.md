# RunnaticsDB - Complete Table & Column Reference

> **Note**: Common audit fields excluded: `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`, `IsDeleted`, `IsActive`

---

## 📋 Core Business Tables

### **Organizations** - Tenant root entity
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| OrganizationName | nvarchar(255) | No | Tenant organization name |
| Code | nvarchar(50) | Yes | Short code identifier |
| Domain | nvarchar(255) | Yes | Organization domain name |
| Email | nvarchar(255) | Yes | Contact email |
| PhoneNumber | nvarchar(50) | Yes | Contact phone |
| Address | nvarchar(MAX) | Yes | Physical address |
| MaxEvents | int | No | Subscription limit for events |
| MaxParticipantsPerEvent | int | No | Max participants per event |
| MaxUsers | int | No | Max user accounts allowed |
| DeletedAt | datetime2 | Yes | Soft delete timestamp |
| DeletedBy | int | Yes | User who deleted record |

---

### **Users** - System users
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| Email | nvarchar(255) | No | Login email (unique) |
| PasswordHash | nvarchar(255) | Yes | Hashed password |
| FirstName | nvarchar(100) | Yes | User first name |
| LastName | nvarchar(100) | Yes | User last name |
| Role | nvarchar(50) | No | User role (Admin, User, etc.) |
| LastLoginAt | datetime2 | Yes | Last login timestamp |

---

### **EventOrganizer** - Event organizer entity
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| Name | nvarchar(255) | No | Organizer name |

---

### **Events** - Main racing events
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| Name | nvarchar(255) | No | Event name |
| Slug | nvarchar(100) | No | URL-friendly identifier |
| Description | nvarchar(MAX) | Yes | Event description |
| EventDate | datetime2 | No | Event start date/time |
| TimeZone | nvarchar(50) | No | Timezone (default: UTC) |
| VenueName | nvarchar(255) | Yes | Venue name |
| VenueAddress | nvarchar(MAX) | Yes | Full venue address |
| VenueLatitude | decimal | Yes | GPS latitude |
| VenueLongitude | decimal | Yes | GPS longitude |
| Status | nvarchar(20) | No | Event status (Draft, Active, etc.) |
| MaxParticipants | int | Yes | Capacity limit |
| RegistrationDeadline | datetime2 | Yes | Registration cutoff |
| Settings | nvarchar(MAX) | Yes | JSON settings |
| EventOrganizerId | int | Yes | Organizer FK |
| EventType | varchar(250) | Yes | Type of event |
| City | nvarchar(100) | Yes | City location |
| State | nvarchar(100) | Yes | State/Province |
| ZipCode | nvarchar(100) | Yes | Postal code |
| Country | nvarchar(100) | Yes | Country |
| BannerImage | nvarchar(MAX) | Yes | Banner image data/URL |
| BannerContentType | nvarchar(50) | Yes | Image MIME type |

---

### **Races** - Individual races within events
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Parent event FK |
| Title | nvarchar(255) | No | Race name (5K, 10K, etc.) |
| Description | nvarchar(MAX) | Yes | Race description |
| Distance | decimal | Yes | Race distance |
| StartTime | datetime2 | Yes | Race start time |
| EndTime | datetime2 | Yes | Race end time |
| MaxParticipants | int | Yes | Race capacity |
| IsTimed | bit | No | Whether timing is enabled |

---

### **Participants** - Registered athletes
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| EventId | int | No | Event FK |
| RaceId | int | Yes | Race FK |
| ImportBatchId | int | Yes | Import batch FK |
| Bib | nvarchar(20) | No | Bib number |
| FirstName | nvarchar(100) | Yes | First name |
| LastName | nvarchar(100) | Yes | Last name |
| Email | nvarchar(255) | Yes | Contact email |
| Phone | nvarchar(50) | Yes | Contact phone |
| DateOfBirth | date | Yes | Birth date |
| Gender | nvarchar(10) | Yes | Gender (M/F/Other) |
| AgeCategory | nvarchar(250) | Yes | Age group |
| Country | nvarchar(100) | Yes | Country |
| State | nvarchar(100) | Yes | State |
| City | nvarchar(100) | Yes | City |
| EmergencyContact | nvarchar(255) | Yes | Emergency contact name |
| EmergencyPhone | nvarchar(50) | Yes | Emergency phone |
| TShirtSize | nvarchar(10) | Yes | T-shirt size |
| RegistrationStatus | nvarchar(20) | Yes | Registration status |
| StartTime | datetime2 | Yes | Individual start time |
| ManualDistance | decimal | Yes | Manual distance override |
| LoopCount | int | Yes | Number of loops completed |
| IsManualTiming | bit | No | Manual timing flag |

---

## 🏷️ RFID Chip Management

### **Chips** - RFID chip inventory
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ChipNumber | nvarchar(50) | Yes | Physical chip number |
| ChipType | nvarchar(50) | Yes | Chip type/model |
| Status | nvarchar(20) | No | Status (Available, Assigned, etc.) |
| PurchaseDate | datetime2 | Yes | Purchase date |
| LastMaintenanceDate | datetime2 | Yes | Last maintenance |
| BatteryLevel | int | Yes | Battery percentage |
| EPC | nvarchar(64) | Yes | Electronic Product Code (unique ID) |
| LastSeenAt | datetime2 | Yes | Last detection timestamp |
| Notes | nvarchar(MAX) | Yes | Notes |
| TenantId | int | Yes | Organization FK |

---

### **ChipAssignments** - Chip-to-participant assignments
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ParticipantId | int | No | Participant FK |
| ChipId | int | No | Chip FK |
| AssignedAt | datetime2 | No | Assignment timestamp |
| ReturnedAt | datetime2 | Yes | Return timestamp |
| Status | nvarchar(20) | No | Assignment status |
| EventId | int | Yes | Event FK |
| AssignedByUserId | int | Yes | User who assigned |
| UnassignedAt | datetime2 | Yes | Unassignment timestamp |

---

## 📡 RFID Reader Hardware

### **ReaderDevices** - RFID reader hardware
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| DeviceName | nvarchar(255) | No | Device name |
| SerialNumber | nvarchar(100) | Yes | Serial number |
| MacAddress | nvarchar(50) | Yes | MAC address |
| IpAddress | nvarchar(50) | Yes | IP address |
| DeviceType | nvarchar(50) | Yes | Device type/model |
| Status | nvarchar(20) | No | Device status |
| LastPingTime | datetime2 | Yes | Last ping |
| FirmwareVersion | nvarchar(50) | Yes | Firmware version |
| Hostname | nvarchar(100) | Yes | Network hostname |
| ConnectionType | int | Yes | Connection type enum |
| LlrpPort | int | Yes | LLRP protocol port |
| RestApiPort | int | Yes | REST API port |
| Username | nvarchar(100) | Yes | Device auth username |
| PasswordHash | nvarchar(255) | Yes | Device auth password |
| ReaderModel | nvarchar(50) | Yes | Reader model |
| ProfileId | int | Yes | Reader profile FK |
| CheckpointId | int | Yes | Assigned checkpoint FK |
| RaceId | int | Yes | Assigned race FK |
| AntennaCount | int | Yes | Number of antennas |
| LastHeartbeat | datetime2 | Yes | Last heartbeat |
| Model | nvarchar(100) | Yes | Model name |
| Notes | nvarchar(MAX) | Yes | Notes |
| PowerLevel | int | Yes | Power level |
| TenantId | int | Yes | Organization FK |

---

### **ReaderProfiles** - Reader configuration profiles
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ProfileName | nvarchar(100) | No | Profile name |
| Description | nvarchar(500) | Yes | Description |
| ReaderMode | nvarchar(50) | No | Reader mode setting |
| SearchMode | nvarchar(50) | No | Search mode setting |
| Session | tinyint | No | Session number (0-3) |
| TagPopulation | int | No | Expected tag population |
| FilterDuplicateReadsMs | int | No | Duplicate filter window (ms) |
| DefaultTxPowerCdBm | int | No | Default transmit power (cdBm) |
| EnableAntennaHub | bit | No | Antenna hub enabled |
| IsDefault | bit | No | Default profile flag |
| AdvancedSettingsJson | nvarchar(MAX) | Yes | Advanced settings JSON |

---

### **ReaderAntennas** - Individual antenna configuration
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ReaderDeviceId | int | No | Reader device FK |
| AntennaPort | tinyint | No | Physical port number |
| AntennaName | nvarchar(100) | Yes | Antenna name |
| TxPowerCdBm | int | No | Transmit power (cdBm) |
| RxSensitivityCdBm | int | No | Receive sensitivity (cdBm) |
| IsEnabled | bit | No | Antenna enabled |
| CheckpointId | int | Yes | Assigned checkpoint FK |
| Position | int | Yes | Physical position |

---

### **ReaderAssignments** - Reader-to-checkpoint mapping
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ReaderDeviceId | int | No | Reader FK |
| CheckpointId | int | No | Checkpoint FK |
| AssignedAt | datetime2 | No | Assignment timestamp |
| RemovedAt | datetime2 | Yes | Removal timestamp |
| Status | nvarchar(20) | No | Assignment status |

---

### **ReaderHealthStatuses** - Real-time health monitoring
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ReaderDeviceId | int | No | Reader FK |
| IsOnline | bit | No | Online status |
| LastHeartbeat | datetime2 | Yes | Last heartbeat |
| CpuTemperatureCelsius | decimal | Yes | CPU temperature |
| AmbientTemperatureCelsius | decimal | Yes | Ambient temperature |
| ReaderMode | int | No | Current reader mode |
| FirmwareVersion | nvarchar(50) | Yes | Firmware version |
| TotalReadsToday | bigint | No | Read count today |
| LastReadTimestamp | datetime2 | Yes | Last read time |
| UptimeSeconds | bigint | Yes | Uptime in seconds |
| MemoryUsagePercent | decimal | Yes | Memory usage % |
| CpuUsagePercent | decimal | Yes | CPU usage % |

---

### **ReaderAlerts** - Reader alerts and issues
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | bigint | No | Primary key (Identity) |
| ReaderDeviceId | int | No | Reader FK |
| AlertType | int | No | Alert type enum |
| Severity | int | No | Severity level |
| Message | nvarchar(500) | No | Alert message |
| Details | nvarchar(MAX) | Yes | Detailed information |
| IsAcknowledged | bit | No | Acknowledgement flag |
| AcknowledgedByUserId | int | Yes | User who acknowledged |
| AcknowledgedAt | datetime2 | Yes | Acknowledgement timestamp |
| ResolutionNotes | nvarchar(1000) | Yes | Resolution notes |
| IsResolved | bit | No | Resolved flag |
| ResolvedAt | datetime2 | Yes | Resolution timestamp |

---

### **ReaderConnectionLogs** - Connection event logs
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | bigint | No | Primary key (Identity) |
| ReaderDeviceId | int | No | Reader FK |
| EventType | int | No | Event type enum |
| ConnectionProtocol | int | Yes | Protocol enum |
| IpAddress | nvarchar(45) | Yes | IP address |
| ErrorMessage | nvarchar(500) | Yes | Error message if any |
| Timestamp | datetime2 | No | Event timestamp |

---

### **Devices** - Legacy device registry
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| DeviceMacAddress | nvarchar(200) | Yes | MAC address |
| TenantId | int | No | Organization FK |
| Name | nvarchar(255) | No | Device name |
| Hostname | nvarchar(100) | Yes | Hostname |
| IpAddress | nvarchar(45) | Yes | IP address |
| FirmwareVersion | nvarchar(50) | Yes | Firmware version |
| ReaderModel | nvarchar(50) | Yes | Reader model |
| IsOnline | bit | No | Online status |
| LastSeenAt | datetime2 | Yes | Last seen timestamp |

---

## 📊 Reading & Timing Data

### **UploadBatches** - Batch upload metadata
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| RaceId | int | Yes | Race FK |
| EventId | int | No | Event FK |
| DeviceId | nvarchar(50) | No | Device identifier |
| ReaderDeviceId | int | Yes | Reader device FK |
| ExpectedCheckpointId | int | Yes | Expected checkpoint FK |
| OriginalFileName | nvarchar(255) | Yes | Original file name |
| StoredFilePath | nvarchar(500) | Yes | Storage path |
| FileSizeBytes | bigint | No | File size |
| FileHash | nvarchar(50) | Yes | File hash (MD5/SHA) |
| FileFormat | nvarchar(20) | No | File format (CSV, DB, etc.) |
| Status | nvarchar(20) | No | Processing status |
| TotalReadings | int | Yes | Total read count |
| UniqueEpcs | int | Yes | Unique chip count |
| TimeRangeStart | bigint | Yes | Earliest timestamp (ms) |
| TimeRangeEnd | bigint | Yes | Latest timestamp (ms) |
| SourceType | nvarchar(20) | No | Source type |
| IsLiveSync | bit | No | Live sync flag |
| ProcessingStartedAt | datetime2 | Yes | Processing start |
| ProcessingCompletedAt | datetime2 | Yes | Processing completion |
| TotalTagsInFile | int | No | Total tags in file |
| TagsProcessed | int | No | Tags processed |

---

### **RawRFIDReadings** - Raw RFID tag reads
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | bigint | No | Primary key (Identity) |
| BatchId | int | No | Upload batch FK |
| DeviceId | nvarchar(50) | No | Device identifier |
| Epc | nvarchar(50) | No | Electronic Product Code |
| TimestampMs | bigint | No | Unix timestamp (ms) |
| Antenna | int | Yes | Antenna port number |
| RssiDbm | decimal | Yes | Signal strength (dBm) |
| Channel | int | Yes | RF channel |
| ReadTimeLocal | datetime2 | No | Local read time |
| ReadTimeUtc | datetime2 | No | UTC read time |
| TimeZoneId | nvarchar(50) | No | Timezone ID |
| ProcessResult | nvarchar(20) | No | Processing result status |
| AssignmentMethod | nvarchar(20) | Yes | Assignment method |
| CheckpointConfidence | decimal | Yes | Confidence score |
| RequiresManualReview | bit | No | Manual review flag |
| IsManualEntry | bit | No | Manual entry flag |
| ManualTimeOverride | datetime2 | Yes | Manual time override |
| DuplicateOfReadingId | bigint | Yes | Duplicate reference |
| ProcessedAt | datetime2 | Yes | Processing timestamp |
| SourceType | nvarchar(20) | No | Source type |
| Notes | nvarchar(MAX) | Yes | Notes |
| IsMultipleEpc | bit | No | Multiple EPC flag |

---

### **ReadingCheckpointAssignments** - Read-to-checkpoint mapping
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ReadingId | bigint | No | Raw reading FK |
| CheckpointId | int | No | Checkpoint FK |

---

### **ReadNormalized** - Normalized participant checkpoint crossings
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Event FK |
| ParticipantId | int | No | Participant FK |
| CheckpointId | int | No | Checkpoint FK |
| RawReadId | bigint | Yes | Raw reading FK |
| ChipTime | datetime2 | No | Chip crossing time |
| GunTime | bigint | Yes | Gun time (ms from start) |
| NetTime | bigint | Yes | Net time (ms) |
| IsManualEntry | bit | No | Manual entry flag |
| ManualEntryReason | nvarchar(500) | Yes | Manual entry reason |
| CreatedByUserId | int | Yes | Creating user FK |
| CreatedDate | datetime2 | No | Creation timestamp |
| UpdatedDate | datetime2 | Yes | Update timestamp |

---

### **Checkpoints** - Race timing points
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Event FK |
| RaceId | int | No | Race FK |
| Name | nvarchar(100) | Yes | Checkpoint name (Start, Mile 5, Finish) |
| DistanceFromStart | decimal | Yes | Distance from start |
| DeviceId | int | No | Device FK |
| ParentDeviceId | int | Yes | Parent device FK |
| IsMandatory | bit | No | Mandatory checkpoint flag |

---

## 🏆 Results & Performance

### **Results** - Final race results
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ParticipantId | int | No | Participant FK |
| EventId | int | No | Event FK |
| RaceId | int | Yes | Race FK |
| StartTime | datetime2 | Yes | Start time |
| FinishTime | bigint | Yes | Finish time (ms) |
| TotalTime | time | Yes | Total time duration |
| OverallPosition | int | Yes | Overall position (deprecated) |
| CategoryPosition | int | Yes | Category position (deprecated) |
| GenderPosition | int | Yes | Gender position (deprecated) |
| Status | nvarchar(20) | No | Result status |
| Notes | nvarchar(500) | Yes | Notes |
| GunTime | bigint | Yes | Gun time (ms) |
| NetTime | bigint | Yes | Net time (ms) |
| OverallRank | int | Yes | Overall ranking |
| GenderRank | int | Yes | Gender ranking |
| CategoryRank | int | Yes | Category ranking |
| DisqualificationReason | nvarchar(500) | Yes | DQ reason |
| IsOfficial | bit | No | Official result flag |
| CertificateGenerated | bit | No | Certificate generated flag |
| ManualFinishTimeMs | bigint | Yes | Manual finish time |
| IsManual | bit | No | Manual result flag |

---

### **SplitTimes** - Segment times between checkpoints
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ParticipantId | int | No | Participant FK |
| FromCheckpointId | int | Yes | Starting checkpoint FK |
| ToCheckpointId | int | Yes | Ending checkpoint FK |
| SplitTime | time | No | Split time duration |
| Distance | decimal | Yes | Segment distance |
| AveragePace | decimal | Yes | Average pace |
| EventId | int | Yes | Event FK |
| CheckpointId | int | Yes | Checkpoint FK |
| ReadNormalizedId | int | Yes | Normalized read FK |
| SplitTimeMs | bigint | Yes | Split time (ms) |
| SegmentTime | bigint | Yes | Segment time (ms) |
| Pace | decimal | Yes | Pace |
| Rank | int | Yes | Overall segment rank |
| GenderRank | int | Yes | Gender segment rank |
| CategoryRank | int | Yes | Category segment rank |
| IsManual | bit | No | Manual entry flag |

---

## ⚙️ Configuration & Settings

### **EventSettings** - Event-level configuration
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Event FK |
| RemoveBanner | bit | No | Remove banner flag |
| Published | bit | No | Published flag |
| RankOnNet | bit | No | Rank on net time |
| ShowResultSummaryForRaces | bit | No | Show result summary |
| UseOldData | bit | No | Use old data flag |
| ConfirmedEvent | bit | No | Confirmed event flag |
| AllowNameCheck | bit | No | Allow name check |
| AllowParticipantEdit | bit | No | Allow participant editing |

---

### **RaceSettings** - Race-specific settings
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| RaceId | int | No | Race FK |
| Published | bit | No | Published flag |
| SendSms | bit | No | Send SMS notifications |
| CheckValidation | bit | No | Validation enabled |
| ShowLeaderboard | bit | No | Show leaderboard |
| ShowResultTable | bit | No | Show result table |
| IsTimed | bit | No | Timing enabled |
| DedUpSeconds | int | Yes | Deduplication window (seconds) |
| EarlyStartCutOff | int | Yes | Early start cutoff (seconds) |
| LateStartCutOff | int | Yes | Late start cutoff (seconds) |
| HasLoops | bit | Yes | Has loops flag |
| LoopLength | decimal | Yes | Loop length |
| DataHeader | nvarchar(MAX) | Yes | Data header JSON |
| PublishDNF | bit | No | Publish DNF results |
| SendCheckpointSms | bit | No | Send checkpoint SMS |
| SendCompletionEmail | bit | No | Send completion email |
| SmsTemplateId | nvarchar(100) | Yes | SMS template ID |
| EmailFromName | nvarchar(100) | Yes | Email from name |
| PassGapThresholdSeconds | int | Yes | Pass gap threshold |

---

### **LeaderBoardSettings** - Leaderboard display settings
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Event FK |
| RaceId | int | Yes | Race FK |
| ShowOverallResults | bit | No | Show overall results |
| ShowCategoryResults | bit | No | Show category results |
| ShowGenderResults | bit | No | Show gender results |
| ShowAgeGroupResults | bit | No | Show age group results |
| EnableLiveLeaderboard | bit | No | Enable live updates |
| ShowSplitTimes | bit | No | Show split times |
| ShowPace | bit | No | Show pace |
| ShowTeamResults | bit | No | Show team results |
| ShowMedalIcon | bit | No | Show medal icons |
| AllowAnonymousView | bit | No | Allow anonymous viewing |
| AutoRefreshIntervalSec | int | No | Auto refresh interval (sec) |
| MaxDisplayedRecords | int | No | Max records to display |
| OverrideSettings | bit | No | Override flag |
| SortByOverallChipTime | bit | Yes | Sort by chip time (overall) |
| SortByOverallGunTime | bit | Yes | Sort by gun time (overall) |
| SortByCategoryChipTime | bit | Yes | Sort by chip time (category) |
| SortByCategoryGunTime | bit | Yes | Sort by gun time (category) |
| NumberOfResultsToShowCategory | int | Yes | Results to show (category) |
| NumberOfResultsToShowOverall | int | Yes | Results to show (overall) |

---

### **AgeCategories** - Age group definitions
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| CategoryName | nvarchar(100) | No | Category name |
| MinAge | int | Yes | Minimum age |
| MaxAge | int | Yes | Maximum age |
| DisplayOrder | int | No | Sort order |

---

## 📜 Certificates

### **CertificateTemplates** - Certificate designs
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| EventId | int | No | Event FK |
| RaceId | int | Yes | Race FK |
| Name | nvarchar(255) | No | Template name |
| Description | nvarchar(1000) | Yes | Description |
| BackgroundImageUrl | nvarchar(500) | Yes | Background image URL |
| BackgroundImageData | nvarchar(MAX) | Yes | Background image data |
| Width | int | No | Certificate width (px) |
| Height | int | No | Certificate height (px) |
| IsDefault | bit | No | Default template flag |

---

### **CertificateFields** - Dynamic certificate fields
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TemplateId | int | No | Template FK |
| FieldType | int | No | Field type enum |
| Content | nvarchar(1000) | Yes | Field content/placeholder |
| XCoordinate | int | No | X position (px) |
| YCoordinate | int | No | Y position (px) |
| Font | nvarchar(100) | No | Font name |
| FontSize | int | No | Font size (pt) |
| FontColor | nvarchar(7) | No | Font color (hex) |
| Width | int | Yes | Field width (px) |
| Height | int | Yes | Field height (px) |
| Alignment | nvarchar(20) | No | Text alignment |
| FontWeight | nvarchar(20) | No | Font weight |
| FontStyle | nvarchar(20) | No | Font style |

---

## 🔔 Notifications

### **NotificationLogs** - Outbound notifications
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | bigint | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| RaceId | int | No | Race FK |
| ParticipantId | int | No | Participant FK |
| EventType | nvarchar(50) | No | Event type |
| Channel | nvarchar(10) | No | Channel (SMS/Email) |
| Recipient | nvarchar(200) | No | Recipient (phone/email) |
| MessageBody | nvarchar(MAX) | No | Message body |
| Subject | nvarchar(200) | Yes | Email subject |
| Status | nvarchar(20) | No | Send status |
| ProviderMessageId | nvarchar(200) | Yes | Provider message ID |
| ErrorMessage | nvarchar(500) | Yes | Error message |
| SentAt | datetime2 | Yes | Sent timestamp |

---

### **Notifications** - In-app notifications
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| UserId | int | Yes | User FK |
| TenantId | int | Yes | Organization FK |
| Title | nvarchar(255) | No | Notification title |
| Message | nvarchar(MAX) | No | Notification message |
| Type | nvarchar(50) | Yes | Notification type |
| IsRead | bit | No | Read flag |
| ReadAt | datetime2 | Yes | Read timestamp |
| ExpiresAt | datetime2 | Yes | Expiration timestamp |

---

## 📥 Data Import

### **ImportBatches** - Participant import batches
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TenantId | int | No | Organization FK |
| EventId | int | No | Event FK |
| FileName | nvarchar(255) | No | Import file name |
| TotalRows | int | No | Total rows |
| ProcessedRows | int | No | Processed rows |
| ErrorRows | int | No | Error rows |
| Status | nvarchar(20) | No | Processing status |
| ErrorLog | nvarchar(MAX) | Yes | Error log |
| CompletedAt | datetime2 | Yes | Completion timestamp |

---

### **ParticipantStaging** - Import staging table
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| ImportBatchId | int | No | Import batch FK |
| RowNumber | int | No | Row number in file |
| Bib | nvarchar(50) | Yes | Bib number |
| FirstName | nvarchar(500) | Yes | First name |
| Gender | nvarchar(50) | Yes | Gender |
| AgeCategory | nvarchar(100) | Yes | Age category |
| Email | nvarchar(255) | Yes | Email |
| Mobile | nvarchar(50) | Yes | Mobile phone |
| ProcessingStatus | nvarchar(20) | No | Processing status |
| ErrorMessage | nvarchar(MAX) | Yes | Error message |
| ParticipantId | int | Yes | Created participant FK |

---

## 🎫 Support System

### **SupportQueries** - Support tickets
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| Subject | nvarchar(255) | No | Ticket subject |
| Body | nvarchar(MAX) | No | Ticket body |
| SubmitterEmail | nvarchar(255) | No | Submitter email |
| StatusId | int | No | Status FK |
| QueryTypeId | int | Yes | Query type FK |
| AssignedToUserId | int | Yes | Assigned user FK |
| SubmitterName | nvarchar(200) | Yes | Submitter name |
| SubmitterPhone | nvarchar(20) | Yes | Submitter phone |
| TenantId | int | Yes | Organization FK |
| RaceId | int | Yes | Race FK |

---

### **SupportQueryComments** - Ticket comments
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| SupportQueryId | int | No | Support query FK |
| CommentText | nvarchar(MAX) | No | Comment text |
| TicketStatusId | int | No | Status FK |
| NotificationSent | bit | No | Notification sent flag |
| CreatedByUserId | int | Yes | User FK |

---

### **SupportQueryStatuses** - Support ticket statuses
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| Name | nvarchar(50) | No | Status name |

---

### **SupportQueryTypes** - Support ticket types
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| Name | nvarchar(100) | No | Type name |

---

## 🔒 Audit & Security

### **AuditLogs** - Change audit trail
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| TableName | nvarchar(100) | No | Table name |
| RecordId | int | No | Record ID |
| Action | nvarchar(50) | No | Action (INSERT, UPDATE, DELETE) |
| OldValues | nvarchar(MAX) | Yes | Old values JSON |
| NewValues | nvarchar(MAX) | Yes | New values JSON |
| UserId | int | Yes | User FK |
| IpAddress | nvarchar(50) | Yes | IP address |
| UserAgent | nvarchar(500) | Yes | User agent string |

---

### **UserSessions** - Active user sessions
| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| Id | int | No | Primary key (Identity) |
| UserId | int | No | User FK |
| TokenHash | nvarchar(255) | No | Session token hash |
| ExpiresAt | datetime2 | No | Expiration timestamp |
| IpAddress | nvarchar(50) | Yes | IP address |
| UserAgent | nvarchar(500) | Yes | User agent string |

---

## 📝 Notes

### **Common Patterns**
- **Primary Keys**: All tables use `Id` as identity column
- **Foreign Keys**: Named with `*Id` suffix
- **Audit Fields** (excluded from above): `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`, `IsDeleted`, `IsActive`
- **Multi-tenancy**: Most tables include `TenantId` referencing `Organizations.Id`
- **Timestamps**: Use `datetime2(7)` for precision
- **Durations**: Stored as `bigint` (milliseconds) or `time` datatype

### **Status Values**
- **Chips**: Available, Assigned, Returned, Lost, Damaged
- **Readers**: Available, Active, Offline, Maintenance
- **ProcessResult**: Pending, Processed, Duplicate, Error
- **UploadBatch**: uploading, processing, completed, failed
- **Results**: Finished, DNF, DSQ

---

**Database**: RunnaticsDB  
**Platform**: Azure SQL Database  
**Purpose**: Race timing and event management with RFID tracking