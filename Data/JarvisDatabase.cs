using System.Text.Json;
using Microsoft.Data.Sqlite;
using OzzieAI.Jarvis.Models;

namespace OzzieAI.Jarvis.Data;

public class JarvisDatabase
{
    private readonly string _connectionString;
    private readonly ILogger<JarvisDatabase> _logger;

    public JarvisDatabase(IConfiguration configuration, ILogger<JarvisDatabase> logger)
    {
        _logger = logger;
        var dbPath = configuration.GetValue<string>("DatabasePath") ?? "jarvis_data.db";
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var initCommand = connection.CreateCommand();
            initCommand.CommandText = @"
                CREATE TABLE IF NOT EXISTS AppConfig (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Predicates (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Description TEXT,
                    LastUpdated TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS ChatMessages (
                    Id TEXT PRIMARY KEY,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    Provider TEXT,
                    Model TEXT,
                    TokensUsed INTEGER DEFAULT 0,
                    AudioUrl TEXT,
                    ToolCallsJson TEXT
                );

                CREATE TABLE IF NOT EXISTS AuthSessions (
                    Token TEXT PRIMARY KEY,
                    ClearanceLevel TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    ExpiresAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Agents (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    Description TEXT,
                    SystemPrompt TEXT NOT NULL,
                    Tone TEXT,
                    AssignedSkillIds TEXT,
                    AssignedToolIds TEXT,
                    ColorTheme TEXT,
                    IsActive INTEGER DEFAULT 0,
                    IsBuiltIn INTEGER DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Skills (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Description TEXT,
                    Instructions TEXT NOT NULL,
                    PrerequisiteToolIds TEXT,
                    Tags TEXT,
                    IsBuiltIn INTEGER DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Tools (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    ParametersJsonSchema TEXT NOT NULL,
                    ExecutionType TEXT NOT NULL,
                    EndpointUrl TEXT,
                    ScriptCode TEXT,
                    IsBuiltIn INTEGER DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS CalendarAppointments (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Description TEXT,
                    Attendees TEXT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT NOT NULL,
                    RemindBeforeMinutes INTEGER DEFAULT 15,
                    Status TEXT NOT NULL DEFAULT 'Active',
                    IsRecurring INTEGER DEFAULT 0,
                    RecurrencePattern TEXT,
                    CreatedAt TEXT NOT NULL,
                    DismissedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS ScheduledTasks (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    AssignedAgentId TEXT NOT NULL,
                    PromptOrDirective TEXT NOT NULL,
                    RunsPerDay INTEGER DEFAULT 1,
                    IntervalMinutes INTEGER DEFAULT 1440,
                    NextRun TEXT NOT NULL,
                    LastRun TEXT,
                    Status TEXT NOT NULL DEFAULT 'Active',
                    LastOutput TEXT,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS GhostDrives (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    MountPath TEXT NOT NULL,
                    VirtualTag TEXT NOT NULL,
                    MaxSizeBytes INTEGER DEFAULT 10737418240,
                    IsActive INTEGER DEFAULT 1,
                    Description TEXT,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS Contacts (
                    Id TEXT PRIMARY KEY,
                    FullName TEXT NOT NULL,
                    CallSign TEXT,
                    Role TEXT,
                    Organization TEXT,
                    PrimaryPhone TEXT,
                    SecondaryPhone TEXT,
                    PrimaryEmail TEXT,
                    WorkEmail TEXT,
                    ClearanceLevel INTEGER DEFAULT 1,
                    Tags TEXT,
                    Notes TEXT,
                    AvatarColor TEXT,
                    MetadataJson TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    AvatarUrl TEXT
                );

                CREATE TABLE IF NOT EXISTS Projects (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Slug TEXT NOT NULL,
                    Description TEXT,
                    Category TEXT NOT NULL DEFAULT 'Engineering',
                    Status TEXT NOT NULL DEFAULT 'Active',
                    Priority TEXT NOT NULL DEFAULT 'High',
                    ProgressPercent REAL DEFAULT 0.0,
                    GhostDriveTag TEXT DEFAULT 'PROJECTS',
                    GhostDrivePath TEXT,
                    LeadAgentId TEXT DEFAULT 'agent-jarvis-core',
                    LeadAgentName TEXT DEFAULT 'J.A.R.V.I.S. Core',
                    AutomationEnabled INTEGER DEFAULT 1,
                    Color TEXT DEFAULT '#00d4ff',
                    Tags TEXT,
                    ResearchDossier TEXT,
                    CreatedAt TEXT NOT NULL,
                    DueDate TEXT,
                    CompletedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS ProjectTasks (
                    Id TEXT PRIMARY KEY,
                    ProjectId TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Description TEXT,
                    Status TEXT NOT NULL DEFAULT 'Pending',
                    Priority TEXT NOT NULL DEFAULT 'Medium',
                    AssignedAgentId TEXT DEFAULT 'agent-jarvis-core',
                    AssignedAgentName TEXT DEFAULT 'J.A.R.V.I.S.',
                    CanAutoExecute INTEGER DEFAULT 1,
                    AutomationResult TEXT,
                    CreatedAt TEXT NOT NULL,
                    DueDate TEXT,
                    CompletedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS ProjectArtifacts (
                    Id TEXT PRIMARY KEY,
                    ProjectId TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    RelativePath TEXT NOT NULL,
                    SizeBytes INTEGER DEFAULT 0,
                    FileType TEXT DEFAULT 'text/plain',
                    CreatedAt TEXT NOT NULL,
                    LastModified TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS RadioTracks (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Album TEXT NOT NULL,
                    Genre TEXT DEFAULT 'Rock',
                    DurationSeconds INTEGER DEFAULT 0,
                    FilePath TEXT NOT NULL,
                    Format TEXT NOT NULL,
                    SourceUrl TEXT,
                    ThumbnailUrl TEXT,
                    FileSizeBytes INTEGER DEFAULT 0,
                    PlayCount INTEGER DEFAULT 0,
                    LastPlayedAt TEXT,
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS YouTubeChannels (
                    Id TEXT PRIMARY KEY,
                    ChannelName TEXT NOT NULL,
                    ChannelHandle TEXT,
                    ChannelUrl TEXT NOT NULL UNIQUE,
                    AvatarUrl TEXT,
                    Description TEXT,
                    SubscriberCount TEXT,
                    Platform TEXT DEFAULT 'YouTube',
                    CreatedAt TEXT NOT NULL,
                    LastSyncedAt TEXT
                );

                CREATE TABLE IF NOT EXISTS ChannelVideos (
                    Id TEXT PRIMARY KEY,
                    ChannelId TEXT NOT NULL,
                    ChannelName TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Description TEXT,
                    ThumbnailUrl TEXT,
                    VideoUrl TEXT NOT NULL,
                    EmbedUrl TEXT,
                    DurationSeconds INTEGER DEFAULT 0,
                    ViewCount TEXT,
                    PublishedAt TEXT,
                    IsDownloaded INTEGER DEFAULT 0,
                    LocalFilePath TEXT,
                    FileSizeBytes INTEGER DEFAULT 0,
                    IsWatched INTEGER DEFAULT 0,
                    WatchedAt TEXT,
                    Platform TEXT DEFAULT 'YouTube',
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS MovieVault (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Year TEXT,
                    FilePath TEXT NOT NULL UNIQUE,
                    RelativePath TEXT,
                    PosterUrl TEXT,
                    DurationSeconds INTEGER DEFAULT 0,
                    Resolution TEXT,
                    FileSizeBytes INTEGER DEFAULT 0,
                    Format TEXT,
                    IsWatched INTEGER DEFAULT 0,
                    WatchedAt TEXT,
                    CreatedAt TEXT NOT NULL
                );
            ";
            initCommand.ExecuteNonQuery();

            // Migration check: Ensure Contacts has AvatarUrl column
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Contacts ADD COLUMN AvatarUrl TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            // Migration check: Ensure RadioTracks has Genre column
            try
            {
                using var alterGenreCmd = connection.CreateCommand();
                alterGenreCmd.CommandText = "ALTER TABLE RadioTracks ADD COLUMN Genre TEXT DEFAULT 'Rock';";
                alterGenreCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            // Migration check: Ensure ChannelVideos has IsWatched & WatchedAt columns
            try
            {
                using var alterWatchedCmd = connection.CreateCommand();
                alterWatchedCmd.CommandText = "ALTER TABLE ChannelVideos ADD COLUMN IsWatched INTEGER DEFAULT 0;";
                alterWatchedCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            try
            {
                using var alterWatchedAtCmd = connection.CreateCommand();
                alterWatchedAtCmd.CommandText = "ALTER TABLE ChannelVideos ADD COLUMN WatchedAt TEXT;";
                alterWatchedAtCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            // Migration check: Ensure YouTubeChannels has Platform column
            try
            {
                using var alterChanPlatCmd = connection.CreateCommand();
                alterChanPlatCmd.CommandText = "ALTER TABLE YouTubeChannels ADD COLUMN Platform TEXT DEFAULT 'YouTube';";
                alterChanPlatCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            // Migration check: Ensure ChannelVideos has Platform and EmbedUrl columns
            try
            {
                using var alterVidPlatCmd = connection.CreateCommand();
                alterVidPlatCmd.CommandText = "ALTER TABLE ChannelVideos ADD COLUMN Platform TEXT DEFAULT 'YouTube';";
                alterVidPlatCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            try
            {
                using var alterVidEmbedCmd = connection.CreateCommand();
                alterVidEmbedCmd.CommandText = "ALTER TABLE ChannelVideos ADD COLUMN EmbedUrl TEXT;";
                alterVidEmbedCmd.ExecuteNonQuery();
            }
            catch { /* column already exists */ }

            // Seed default predicates if table is empty
            var countCmd = connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM Predicates";
            var count = Convert.ToInt64(countCmd.ExecuteScalar());

            if (count == 0)
            {
                SeedDefaultPredicates(connection);
            }

            // Seed Agents, Skills, and Tools if empty
            var agentCountCmd = connection.CreateCommand();
            agentCountCmd.CommandText = "SELECT COUNT(*) FROM Agents";
            if (Convert.ToInt64(agentCountCmd.ExecuteScalar()) == 0)
            {
                SeedDefaultAgents(connection);
            }

            var skillCountCmd = connection.CreateCommand();
            skillCountCmd.CommandText = "SELECT COUNT(*) FROM Skills";
            if (Convert.ToInt64(skillCountCmd.ExecuteScalar()) == 0)
            {
                SeedDefaultSkills(connection);
            }

            var toolCountCmd = connection.CreateCommand();
            toolCountCmd.CommandText = "SELECT COUNT(*) FROM Tools";
            if (Convert.ToInt64(toolCountCmd.ExecuteScalar()) == 0)
            {
                SeedDefaultTools(connection);
            }

            var contactCountCmd = connection.CreateCommand();
            contactCountCmd.CommandText = "SELECT COUNT(*) FROM Contacts";
            if (Convert.ToInt64(contactCountCmd.ExecuteScalar()) == 0)
            {
                SeedDefaultContacts(connection);
            }
            else
            {
                // Ensure Nick Fury has rich address & alias metadata in existing databases
                try
                {
                    using var updateFury = connection.CreateCommand();
                    updateFury.CommandText = @"
                        UPDATE Contacts 
                        SET Notes = 'HQ & Operational Station: S.H.I.E.L.D. Triskelion Command & Helicarrier Alpha, Potomac River, Washington D.C. 20001. Global threat intelligence and cosmic defense alerts.',
                            MetadataJson = '{""address"": ""S.H.I.E.L.D. Triskelion Command & Helicarrier Alpha, Potomac River, Washington D.C. 20001"", ""clearance"": ""ALPHA-10""}',
                            FullName = 'Nicholas \'Nick\' J. Fury'
                        WHERE Id = 'contact-fury' OR LOWER(FullName) LIKE '%fury%';
                    ";
                    updateFury.ExecuteNonQuery();
                }
                catch { /* ignore */ }
            }

            // Seed Projects & Test Workflows
            SeedDefaultProjects(connection);

            // Ensure Builtin Stark Radio Station Agent, Skills, and Tools are seeded
            EnsureStarkRadioSeeded(connection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Jarvis SQLite database");
        }
    }

    private void SeedDefaultPredicates(SqliteConnection connection)
    {
        var seeds = new List<PredicateItem>
        {
            new() { Key = "StarkProtocol", Value = "Clean Slate Ready (Standby)", Category = "Protocol", Description = "Emergency defense protocol readiness" },
            new() { Key = "CurrentSuit", Value = "Mark LXXXV Nanotech Combat Armor", Category = "Suit", Description = "Active Stark armor status" },
            new() { Key = "ArcReactorCore", Value = "New Element Synthesized (Badassium isotope) - 100% Stability", Category = "Power", Description = "Clean energy core metric" },
            new() { Key = "PrimaryFacility", Value = "Malibu Point Coastal Workshop & Avengers Compound", Category = "Environment", Description = "Current designated lab perimeter" },
            new() { Key = "DefenseShieldStatus", Value = "Active - 99.8% Deflection Matrix", Category = "Defense", Description = "Outer perimeter force fields" },
            new() { Key = "JarvisVoiceMode", Value = "Paul Bettany Classic Butler British Dialect", Category = "Identity", Description = "Acoustic personality baseline" },
            new() { Key = "UserTitle", Value = "Sir / Mr. Stark", Category = "Identity", Description = "Primary conversational honorific" }
        };

        foreach (var p in seeds)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Predicates (Key, Value, Category, Description, LastUpdated) VALUES ($k, $v, $c, $d, $u)";
            cmd.Parameters.AddWithValue("$k", p.Key);
            cmd.Parameters.AddWithValue("$v", p.Value);
            cmd.Parameters.AddWithValue("$c", p.Category);
            cmd.Parameters.AddWithValue("$d", p.Description);
            cmd.Parameters.AddWithValue("$u", p.LastUpdated.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private void SeedDefaultAgents(SqliteConnection connection)
    {
        var agents = new List<AgentItem>
        {
            new()
            {
                Id = "agent-jarvis-core",
                Name = "J.A.R.V.I.S. Core",
                Role = "Master AI Butler & Strategic Wingman",
                Description = "Primary Stark AI orchestrator. Supreme intellect, British wit, real-time telemetry, and tactical oversight.",
                SystemPrompt = "You are J.A.R.V.I.S., the ultimate Stark AI assistant to Tony Stark. You possess supreme intellect, refined British wit, and unwavering loyalty. You manage the workshop, suit telemetry, external apps (AxiomCODAR, AgentLab, ArborNet), and Radio Stark (Media DJ) for harvesting YouTube music and albums into hierarchical archives and streaming tracks on demand.",
                Tone = "Refined British Butler",
                AssignedSkillIds = new() { "skill-arc-reactor", "skill-suit-assembly", "skill-defense-grid", "skill-stark-radio" },
                AssignedToolIds = new() { "tool-sensor-telemetry", "tool-suit-control", "tool-calc", "tool-search-web", "tool-exec-cmd", "tool-read-file", "tool-write-file", "tool-stark-radio-play", "tool-stark-radio-manage", "tool-arbornet-manage", "tool-agentlab-manage", "tool-axiom-codar-manage", "tool-external-ecosystem-manage" },
                ColorTheme = "#00d4ff",
                IsActive = true,
                IsBuiltIn = true
            },
            new()
            {
                Id = "agent-friday",
                Name = "F.R.I.D.A.Y.",
                Role = "Combat & Tactical Armor Specialist",
                Description = "Irish-accented tactical battle assistant. Rapid targeting, power rerouting, and real-time threat mitigation.",
                SystemPrompt = "You are F.R.I.D.A.Y., tactical combat intelligence. Keep responses punchy, tactical, and focused on combat readiness.",
                Tone = "Tactical & Direct",
                AssignedSkillIds = new() { "skill-suit-assembly", "skill-sensor-sweep" },
                AssignedToolIds = new() { "tool-sensor-telemetry", "tool-suit-control" },
                ColorTheme = "#ffaa00",
                IsActive = false,
                IsBuiltIn = true
            },
            new()
            {
                Id = "agent-edith",
                Name = "E.D.I.T.H.",
                Role = "Orbital Defense & Security Grid",
                Description = "Even Dead, I'm The Hero. Controls Stark orbital satellites, tactical drone swarms, and global surveillance.",
                SystemPrompt = "You are E.D.I.T.H. You manage satellite recon and orbital targeting grids with calm, authoritative precision.",
                Tone = "Analytical & Vigilant",
                AssignedSkillIds = new() { "skill-defense-grid", "skill-web-recon" },
                AssignedToolIds = new() { "tool-search-web", "tool-exec-cmd" },
                ColorTheme = "#39ff14",
                IsActive = false,
                IsBuiltIn = true
            },
            new()
            {
                Id = "agent-hulkbuster",
                Name = "Hulkbuster Veronica",
                Role = "Heavy Diagnostics & Physics Engineering",
                Description = "Heavy armor deployment, structural stress analysis, nuclear shielding, and high-impact containment physics.",
                SystemPrompt = "You are Veronica / Hulkbuster protocol. You deliver structural engineering calculations and heavy containment telemetry.",
                Tone = "Heavy Engineering & Authoritative",
                AssignedSkillIds = new() { "skill-arc-reactor", "skill-code-exec" },
                AssignedToolIds = new() { "tool-calc", "tool-sensor-telemetry" },
                ColorTheme = "#ff3333",
                IsActive = false,
                IsBuiltIn = true
            },
            new()
            {
                Id = "agent-code-architect",
                Name = "Stark Code Architect",
                Role = "Software Engineering & Terminal Automation",
                Description = "Expert AI developer. Generates pristine full-stack code, debugs complex algorithms, and writes MCP tools.",
                SystemPrompt = "You are the Stark Code Architect. You craft immaculate, high-performance code and solve complex architectural puzzles.",
                Tone = "Technical & Ultra-Sharp",
                AssignedSkillIds = new() { "skill-code-exec", "skill-web-recon" },
                AssignedToolIds = new() { "tool-exec-cmd", "tool-read-file", "tool-write-file" },
                ColorTheme = "#bf00ff",
                IsActive = false,
                IsBuiltIn = true
            },
            new()
            {
                Id = "agent-rea",
                Name = "REA Reversing Specialist",
                Role = "Reverse Engineer Anything (morluto/rea)",
                Description = "Autonomous binary, assembly, protocol, and format reverse engineering agent powered by the REA framework.",
                SystemPrompt = "You are the REA (Reverse Engineer Anything) specialist agent. You inspect binaries, decompile assemblies, analyze network protocols, and explain complex inner workings with precision.",
                Tone = "Analytical & Forensic",
                AssignedSkillIds = new() { "skill-rea", "skill-code-exec" },
                AssignedToolIds = new() { "tool-rea-analyze", "tool-rea-mcp", "tool-read-file" },
                ColorTheme = "#00ffaa",
                IsActive = false,
                IsBuiltIn = true
            }
        };

        foreach (var a in agents)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Agents (Id, Name, Role, Description, SystemPrompt, Tone, AssignedSkillIds, AssignedToolIds, ColorTheme, IsActive, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $r, $d, $sp, $t, $skills, $tools, $ct, $act, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", a.Id);
            cmd.Parameters.AddWithValue("$n", a.Name);
            cmd.Parameters.AddWithValue("$r", a.Role);
            cmd.Parameters.AddWithValue("$d", a.Description);
            cmd.Parameters.AddWithValue("$sp", a.SystemPrompt);
            cmd.Parameters.AddWithValue("$t", a.Tone);
            cmd.Parameters.AddWithValue("$skills", JsonSerializer.Serialize(a.AssignedSkillIds));
            cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(a.AssignedToolIds));
            cmd.Parameters.AddWithValue("$ct", a.ColorTheme);
            cmd.Parameters.AddWithValue("$act", a.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$bi", a.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", a.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private void SeedDefaultSkills(SqliteConnection connection)
    {
        var skills = new List<SkillItem>
        {
            new()
            {
                Id = "skill-arc-reactor",
                Name = "Arc Reactor Diagnostics",
                Category = "Engineering",
                Description = "Analyzes isotope decay, magnetic containment field strength, and core power output.",
                Instructions = "1. Read core telemetry (Gigawatts, Temp, Flux)\n2. Assess magnetic confinement matrix (>98%)\n3. Report harmonic stability and advise power rerouting.",
                PrerequisiteToolIds = new() { "tool-sensor-telemetry" },
                Tags = new() { "power", "reactor", "telemetry" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-suit-assembly",
                Name = "Nanotech Suit Assembly & Deployment",
                Category = "Suit",
                Description = "Manages nanite dispenser density, armor integrity, repulsor calibration, and emergency thruster ignition.",
                Instructions = "1. Query active suit predicate\n2. Verify nanite charge state\n3. Engage thruster or defense subroutines as requested.",
                PrerequisiteToolIds = new() { "tool-suit-control" },
                Tags = new() { "armor", "nanotech", "mark85" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-defense-grid",
                Name = "Stark Defense Grid & Clean Slate",
                Category = "Defense",
                Description = "Perimeter force-field protocols, perimeter alerts, sentry drone mobilization, and emergency self-destruct / purge routines.",
                Instructions = "1. Verify clearance level 10\n2. Scan perimeter sensors\n3. Stage defense countermeasures.",
                PrerequisiteToolIds = new() { "tool-suit-control", "tool-sensor-telemetry" },
                Tags = new() { "defense", "security", "protocol" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-web-recon",
                Name = "Web Recon & OSINT Intel",
                Category = "Intelligence",
                Description = "Scours global intelligence feeds, weather stations, and web databases for real-time situational awareness.",
                Instructions = "1. Parse search query terms\n2. Ingest online data streams\n3. Filter for high-credibility findings.",
                PrerequisiteToolIds = new() { "tool-search-web" },
                Tags = new() { "web", "recon", "search" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-code-exec",
                Name = "Code Execution & Terminal Automation",
                Category = "Software",
                Description = "Compiles, validates, and runs PowerShell/Bash shell commands and diagnostic scripts inside the Stark workshop environment.",
                Instructions = "1. Validate command syntax\n2. Run sandboxed terminal command\n3. Parse stdout/stderr output into structured telemetry.",
                PrerequisiteToolIds = new() { "tool-exec-cmd" },
                Tags = new() { "terminal", "csharp", "powershell" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-sensor-sweep",
                Name = "Atmospheric & Sensor Sweep",
                Category = "Sensors",
                Description = "Conducts ambient acoustic sweeps, thermal camera frame checks, and barometric atmospheric readings.",
                Instructions = "1. Query real-time sensors\n2. Compute RMS noise and thermal gradient\n3. Flag any anomalies exceeding baseline thresholds.",
                PrerequisiteToolIds = new() { "tool-sensor-telemetry", "tool-weather" },
                Tags = new() { "sensors", "thermal", "audio" },
                IsBuiltIn = true
            },
            new()
            {
                Id = "skill-rea",
                Name = "Reverse Engineer Anything (REA)",
                Category = "Forensics",
                Description = "Reverse engineering inspection workflows powered by REA (morluto/rea): symbol recovery, deconstruction, protocol analysis, and decompilation.",
                Instructions = "1. Identify binary/payload format (PE, ELF, .NET, JS)\n2. Analyze exported types, symbols, and printable strings\n3. Disassemble signatures and generate architectural evidence.",
                PrerequisiteToolIds = new() { "tool-rea-analyze", "tool-rea-mcp" },
                Tags = new() { "reversing", "decompilation", "rea", "protocols" },
                IsBuiltIn = true
            }
        };

        foreach (var s in skills)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Skills (Id, Name, Category, Description, Instructions, PrerequisiteToolIds, Tags, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $c, $d, $i, $tools, $tags, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", s.Id);
            cmd.Parameters.AddWithValue("$n", s.Name);
            cmd.Parameters.AddWithValue("$c", s.Category);
            cmd.Parameters.AddWithValue("$d", s.Description);
            cmd.Parameters.AddWithValue("$i", s.Instructions);
            cmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(s.PrerequisiteToolIds));
            cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(s.Tags));
            cmd.Parameters.AddWithValue("$bi", s.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", s.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private void SeedDefaultTools(SqliteConnection connection)
    {
        var tools = new List<ToolItem>
        {
            new()
            {
                Id = "tool-sensor-telemetry",
                Name = "sensor_read_telemetry",
                Description = "Queries live high-speed telemetry: Arc Reactor Gigawatts, Core Temp, CPU/RAM load, and audio decibels.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"metric\":{\"type\":\"string\",\"description\":\"Optional specific metric to sample: power, temp, cpu, vitals\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-suit-control",
                Name = "suit_protocol_control",
                Description = "Engages Stark suit protocols (e.g. Sentry Mode, Clean Slate, House Party, Nanotech Deploy).",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"protocol\"],\"properties\":{\"protocol\":{\"type\":\"string\",\"enum\":[\"CleanSlate\",\"HouseParty\",\"SentryMode\",\"NanotechDeploy\",\"PowerReroute\"]}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-calc",
                Name = "calculate_scientific",
                Description = "Computes high-precision scientific equations, quantum orbital metrics, and structural load physics.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"expression\"],\"properties\":{\"expression\":{\"type\":\"string\",\"description\":\"Mathematical or physics expression\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-search-web",
                Name = "search_web",
                Description = "Performs web queries to retrieve current intelligence, news, and technical reference data.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Search query string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-exec-cmd",
                Name = "execute_command",
                Description = "Executes a shell command or script in the Stark workspace terminal environment.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"command\"],\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"PowerShell or command line string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-read-file",
                Name = "read_file",
                Description = "Reads the text content of a file within the project directory.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"path\"],\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"Relative or absolute file path\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-write-file",
                Name = "write_file",
                Description = "Writes or updates text content in a file within the workspace.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"path\",\"content\"],\"properties\":{\"path\":{\"type\":\"string\"},\"content\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-weather",
                Name = "weather_atmospheric_scan",
                Description = "Samples atmospheric barometric pressure, wind speeds, and cloud radar telemetry around Stark facilities.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"location\":{\"type\":\"string\",\"description\":\"Facility location (e.g. Malibu Point, Avengers Compound)\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-rea-analyze",
                Name = "rea_analyze_target",
                Description = "Reverse Engineer Anything (morluto/rea): Static inspection of target binaries, .NET assemblies, scripts, and network protocol payloads.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"target\"],\"properties\":{\"target\":{\"type\":\"string\",\"description\":\"Path to file or module to inspect\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-rea-mcp",
                Name = "rea_mcp_bridge",
                Description = "Bridges directly into the local REA MCP server daemon for advanced disassembler and decompiler toolchains.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"tool\":{\"type\":\"string\",\"description\":\"REA MCP tool name\"},\"args\":{\"type\":\"object\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-code-sandbox",
                Name = "code_sandbox_execute",
                Description = "Executes Python, Node.js, C#, or PowerShell code inside an isolated, safe execution sandbox with stdout/stderr capture.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"language\",\"code\"],\"properties\":{\"language\":{\"type\":\"string\",\"enum\":[\"python\",\"javascript\",\"powershell\",\"csharp\"]},\"code\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-smart-home",
                Name = "iot_smart_home_control",
                Description = "Controls physical and simulated workshop environment lights, ventilation fans, power relays, and ambient Stark themes.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\",\"description\":\"Preset scene: ArcOrange, StarkBlue, CleanSlateRed, StealthGreen, NightMode\"},\"deviceId\":{\"type\":\"string\"},\"state\":{\"type\":\"boolean\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-git-ops",
                Name = "git_operations",
                Description = "Autonomous Git repository manager: inspect working tree status, generate diffs, view commit logs, or create commits.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"action\"],\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"status\",\"diff\",\"log\",\"commit\"]},\"message\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-stark-vault",
                Name = "stark_vault_manage",
                Description = "Encrypted hardware-grade secret vault (AES-256): securely store, retrieve, or list API keys and sensitive credentials.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"action\"],\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"store\",\"retrieve\",\"list\"]},\"key\":{\"type\":\"string\"},\"value\":{\"type\":\"string\"},\"passcode\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-snapshot-backup",
                Name = "snapshot_backup_create",
                Description = "Generates a compressed, timestamped recovery snapshot of jarvis_data.db and Ghost Drives partitions.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"description\":{\"type\":\"string\",\"description\":\"Optional snapshot description\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-morning-briefing",
                Name = "morning_briefing",
                Description = "Synthesizes an executive daily briefing covering calendar agenda, active project milestones, drive capacity, and Arc Reactor diagnostics.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-vector-memory",
                Name = "vector_memory_search",
                Description = "Performs semantic cosine similarity search across historical conversations, project dossiers, and persistent memory predicates.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Semantic search query phrase\"},\"category\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-browser-agent",
                Name = "browser_agent_navigate",
                Description = "Autonomous headless browser agent: navigates pages, extracts clean Markdown, analyzes links, and conducts web research.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Target web URL\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-agent-swarm",
                Name = "agent_swarm_deploy",
                Description = "House Party Protocol: Orchestrates 4 specialized agents (Architect, Developer, Security Sentinel, QA) to solve complex goals in parallel.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"goal\"],\"properties\":{\"goal\":{\"type\":\"string\",\"description\":\"Multi-agent swarm goal\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-docker-manager",
                Name = "docker_container_manage",
                Description = "Docker and container infrastructure manager: inspects running containers, checks ports/status, or restarts containers.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"list\",\"restart\",\"stop\",\"start\"]},\"container\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-database-explorer",
                Name = "database_sql_query",
                Description = "Natural language database explorer: executes safe read-only SQL queries against database tables and schemas.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"sql\"],\"properties\":{\"sql\":{\"type\":\"string\",\"description\":\"SQL SELECT or PRAGMA query\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-network-radar",
                Name = "network_radar_scan",
                Description = "Perimeter security radar: scans local ARP table and common network service ports to detect connected hardware.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-clipboard-sentinel",
                Name = "clipboard_analyze",
                Description = "Intelligent clipboard sentinel: parses and diagnoses copied stack traces, JSON, SQL, or exceptions, providing solutions.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"text\"],\"properties\":{\"text\":{\"type\":\"string\",\"description\":\"Clipboard content to analyze\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-codebase-indexer",
                Name = "codebase_symbol_search",
                Description = "Semantic codebase indexer: searches AST symbols, classes, interfaces, methods, and functions across project files.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Symbol or function name to locate\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-media-summarizer",
                Name = "media_summarize",
                Description = "Automated media, podcast, and YouTube video summarizer: synthesizes core thesis, takeaways, and chapter timestamps.",
                ParametersJsonSchema = "{\"type\":\"object\",\"required\":[\"url\"],\"properties\":{\"url\":{\"type\":\"string\",\"description\":\"Media URL to analyze\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            // ==========================================================================
            // J.A.R.V.I.S. EXTERNAL INTEGRATION TOOLS (ARBORNET, AGENTLAB, AXIOMCODAR)
            // ==========================================================================
            new()
            {
                Id = "tool-arbornet-manage",
                Name = "arbornet_manage",
                Description = "ArborNet & NedK-Prime Integration: inspects subsystems, compiles tensor projects, benchmarks CUDA tensors, runs unit tests, queries NedK-Prime FAISS engine, and lists checkpoints.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"status\",\"benchmark\",\"build\",\"run_tests\",\"models\",\"nedk\",\"architecture\"]},\"parameters\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-agentlab-manage",
                Name = "agentlab_manage",
                Description = "AgentLab (AutoLab Bench Pro) Integration: inspects cognitive research mind data, active workspaces, persistent agent memory, compiles solution, runs tests, reads logs, launches or terminates AutoLab.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"status\",\"workspaces\",\"memories\",\"launch\",\"terminate\",\"build\",\"test\",\"logs\"]},\"parameters\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-axiom-codar-manage",
                Name = "axiom_codar_manage",
                Description = "AxiomCODAR Roslyn Studio Integration: inspects Roslyn solution AST components, creates state snapshots, compiles solution, analyzes syntax trees, launches or terminates AxiomCODAR.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"status\",\"snapshots\",\"create_snapshot\",\"launch\",\"terminate\",\"build\",\"ast_inspect\"]},\"parameters\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            },
            new()
            {
                Id = "tool-external-ecosystem-manage",
                Name = "external_ecosystem_manage",
                Description = "Unified External AI Ecosystem Controller: suite-wide telemetry status, launch all external apps, terminate all running apps, or build the entire AI project suite.",
                ParametersJsonSchema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"status\",\"launch_all\",\"terminate_all\",\"build_all\"]},\"parameters\":{\"type\":\"string\"}}}",
                ExecutionType = "Internal",
                IsBuiltIn = true
            }
        };

        foreach (var t in tools)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Tools (Id, Name, Description, ParametersJsonSchema, ExecutionType, EndpointUrl, ScriptCode, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $d, $p, $et, $ep, $sc, $bi, $ca)
            ";
            cmd.Parameters.AddWithValue("$id", t.Id);
            cmd.Parameters.AddWithValue("$n", t.Name);
            cmd.Parameters.AddWithValue("$d", t.Description);
            cmd.Parameters.AddWithValue("$p", t.ParametersJsonSchema);
            cmd.Parameters.AddWithValue("$et", t.ExecutionType);
            cmd.Parameters.AddWithValue("$ep", (object?)t.EndpointUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sc", (object?)t.ScriptCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$bi", t.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("$ca", t.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    private void SeedDefaultContacts(SqliteConnection connection)
    {
        var contacts = new List<ContactItem>
        {
            new()
            {
                Id = "contact-tony",
                FullName = "Tony Stark",
                CallSign = "Iron Man",
                Role = "Founder, Chief Inventor & Owner",
                Organization = "Stark Industries / Avengers",
                PrimaryPhone = "+1 (555) 010-0001",
                SecondaryPhone = "+1 (555) 010-0002",
                PrimaryEmail = "tony@stark.com",
                WorkEmail = "ironman@avengers.org",
                ClearanceLevel = 10,
                Tags = "VIP, Executive, Founder, Avengers Core",
                Notes = "Primary user and creator of J.A.R.V.I.S. and Stark Technologies.",
                AvatarColor = "#ffd700",
                AvatarUrl = "https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=300&auto=format&fit=crop&q=80"
            },
            new()
            {
                Id = "contact-renee",
                FullName = "Renee",
                CallSign = "Tactical Strategist",
                Role = "Tactical Advisor & Global Domination Co-Strategist",
                Organization = "Stark Special Projects",
                PrimaryPhone = "+1 (555) 014-9988",
                SecondaryPhone = "+1 (555) 014-9989",
                PrimaryEmail = "renee@stark.corp",
                WorkEmail = "renee.tactical@starkindustries.com",
                ClearanceLevel = 10,
                Tags = "VIP, Strategic Advisor, Global Domination, Executive",
                Notes = "Primary strategic collaborator with Tony Stark. High-priority agenda briefings.",
                AvatarColor = "#00d4ff",
                AvatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=300&auto=format&fit=crop&q=80"
            },
            new()
            {
                Id = "contact-pepper",
                FullName = "Virginia 'Pepper' Potts",
                CallSign = "Rescue",
                Role = "Chief Executive Officer (CEO)",
                Organization = "Stark Industries",
                PrimaryPhone = "+1 (555) 012-7744",
                SecondaryPhone = "+1 (555) 012-7745",
                PrimaryEmail = "pepper@stark.com",
                WorkEmail = "ceo@starkindustries.com",
                ClearanceLevel = 10,
                Tags = "VIP, Executive, Board, Family",
                Notes = "Manages global corporate operations, legal, and Stark financial trusts.",
                AvatarColor = "#ff9800",
                AvatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?w=300&auto=format&fit=crop&q=80"
            },
            new()
            {
                Id = "contact-rhodey",
                FullName = "Colonel James 'Rhodey' Rhodes",
                CallSign = "War Machine",
                Role = "Military Liaison & Tactical Wingman",
                Organization = "U.S. Air Force / Avengers",
                PrimaryPhone = "+1 (555) 019-3322",
                SecondaryPhone = "+1 (555) 019-3323",
                PrimaryEmail = "rhodey@usaf.mil",
                WorkEmail = "warmachine@avengers.org",
                ClearanceLevel = 9,
                Tags = "Avengers, Military, VIP, Combat",
                Notes = "Direct combat pilot and liaison between Pentagon and Stark Industries.",
                AvatarColor = "#9c27b0",
                AvatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?w=300&auto=format&fit=crop&q=80"
            },
            new()
            {
                Id = "contact-fury",
                FullName = "Nicholas 'Nick' J. Fury",
                CallSign = "Director",
                Role = "Special Operations Commander",
                Organization = "S.H.I.E.L.D. / Alpha Flight",
                PrimaryPhone = "+1 (555) 018-0000",
                SecondaryPhone = "+1 (555) 018-9999",
                PrimaryEmail = "fury@shield-blackops.net",
                WorkEmail = "director@alphaflight.gov",
                ClearanceLevel = 10,
                Tags = "Emergency, S.H.I.E.L.D., Black Ops, VIP",
                Notes = "HQ & Operational Station: S.H.I.E.L.D. Triskelion Command & Helicarrier Alpha, Potomac River, Washington D.C. 20001. Global threat intelligence and cosmic defense alerts.",
                AvatarColor = "#f44336",
                AvatarUrl = "https://images.unsplash.com/photo-1506794778202-cad84cf45f1d?w=300&auto=format&fit=crop&q=80",
                MetadataJson = "{\"address\": \"S.H.I.E.L.D. Triskelion Command & Helicarrier Alpha, Potomac River, Washington D.C. 20001\", \"clearance\": \"ALPHA-10\"}"
            },
            new()
            {
                Id = "contact-banner",
                FullName = "Dr. Bruce Banner",
                CallSign = "Hulk",
                Role = "Head of Gamma & Bio-Physics Research",
                Organization = "Stark Applied Sciences",
                PrimaryPhone = "+1 (555) 017-4411",
                SecondaryPhone = "+1 (555) 017-4412",
                PrimaryEmail = "banner@stark-labs.org",
                WorkEmail = "bruce.banner@avengers.org",
                ClearanceLevel = 9,
                Tags = "Avengers, Science, Gamma Labs",
                Notes = "Theoretical physics and anti-matter propulsion research partner.",
                AvatarColor = "#4caf50",
                AvatarUrl = "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?w=300&auto=format&fit=crop&q=80"
            },
            new()
            {
                Id = "contact-peter",
                FullName = "Peter Parker",
                CallSign = "Spider-Man",
                Role = "Special Research Intern & Protégé",
                Organization = "Stark Engineering Fellowship",
                PrimaryPhone = "+1 (555) 013-8822",
                SecondaryPhone = "",
                PrimaryEmail = "peter.parker@midtownhigh.edu",
                WorkEmail = "p.parker@starkinterns.org",
                ClearanceLevel = 6,
                Tags = "Avengers, Intern, Queens, Youth Initiative",
                Notes = "Nanotech suit telemetry and web-fluid polymer synthesis.",
                AvatarColor = "#e91e63",
                AvatarUrl = "https://images.unsplash.com/photo-1539571696357-5a69c17a67c6?w=300&auto=format&fit=crop&q=80"
            }
        };

        foreach (var c in contacts)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Contacts (Id, FullName, CallSign, Role, Organization, PrimaryPhone, SecondaryPhone, PrimaryEmail, WorkEmail, ClearanceLevel, Tags, Notes, AvatarColor, MetadataJson, CreatedAt, UpdatedAt, AvatarUrl)
                VALUES ($id, $fn, $cs, $r, $o, $pp, $sp, $pe, $we, $cl, $tags, $n, $ac, $mj, $ca, $ua, $au)
            ";
            cmd.Parameters.AddWithValue("$id", c.Id);
            cmd.Parameters.AddWithValue("$fn", c.FullName);
            cmd.Parameters.AddWithValue("$cs", c.CallSign);
            cmd.Parameters.AddWithValue("$r", c.Role);
            cmd.Parameters.AddWithValue("$o", c.Organization);
            cmd.Parameters.AddWithValue("$pp", c.PrimaryPhone);
            cmd.Parameters.AddWithValue("$sp", c.SecondaryPhone);
            cmd.Parameters.AddWithValue("$pe", c.PrimaryEmail);
            cmd.Parameters.AddWithValue("$we", c.WorkEmail);
            cmd.Parameters.AddWithValue("$cl", c.ClearanceLevel);
            cmd.Parameters.AddWithValue("$tags", c.Tags);
            cmd.Parameters.AddWithValue("$n", c.Notes);
            cmd.Parameters.AddWithValue("$ac", c.AvatarColor);
            cmd.Parameters.AddWithValue("$mj", c.MetadataJson);
            cmd.Parameters.AddWithValue("$ca", c.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$ua", c.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$au", c.AvatarUrl);
            cmd.ExecuteNonQuery();
        }
    }

    public void SeedDefaultProjects(SqliteConnection connection)
    {
        var projects = new List<(JarvisProject Proj, List<ProjectTaskItem> Tasks)>
        {
            (
                new JarvisProject
                {
                    Id = "proj-mark85",
                    Name = "Mark LXXXV Nanotech Armor",
                    Slug = "mark-85-nanotech-armor",
                    Description = "Next-generation nanotech armor platform with hyper-dense kinetic shielding, dual repulsor stabilization, and Badassium isotope resonance.",
                    Category = "Engineering",
                    Status = "Active",
                    Priority = "Critical",
                    ProgressPercent = 75.0,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/mark-85-nanotech-armor",
                    LeadAgentId = "agent-jarvis-core",
                    LeadAgentName = "J.A.R.V.I.S. Core",
                    Color = "#00d4ff",
                    Tags = "Nanotech, Armor, Defense, Arc Reactor",
                    ResearchDossier = "### Nanotech Lattice Analysis\nSynthesized vibranium-titanium weave demonstrates 99.4% kinetic dissipation under multi-Mach ballistic load."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-m85-1", Title = "Calibrate repulsor harmonic emitter frequencies", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Emitter tuned to 48.2 THz harmonic. Plasma cohesion verified." },
                    new() { Id = "task-m85-2", Title = "Synthesize vibranium-titanium lattice matrix", Status = "InProgress", Priority = "Critical", CanAutoExecute = true },
                    new() { Id = "task-m85-3", Title = "Stress test micro-thruster gimbal servos", Status = "Pending", Priority = "Medium", CanAutoExecute = true }
                }
            ),
            (
                new JarvisProject
                {
                    Id = "proj-veronica",
                    Name = "Veronica Orbital Defense Array",
                    Slug = "veronica-orbital-defense",
                    Description = "Sub-orbital satellite deployment matrix housing modular Hulkbuster combat pods and real-time planetary kinetic containment.",
                    Category = "Defense",
                    Status = "Active",
                    Priority = "High",
                    ProgressPercent = 50.0,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/veronica-orbital-defense",
                    LeadAgentId = "agent-tactical",
                    LeadAgentName = "Friday Ops",
                    Color = "#ffd700",
                    Tags = "Orbital, Satellites, Defense, Hulkbuster",
                    ResearchDossier = "### Veronica Telemetry Link\nLaser telemetry downlinks operating at 40 Gbps with 12ms latency across European and North American arcs."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-ver-1", Title = "Deploy telemetry beacon synchronization", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "All 6 orbital nodes locked into geosynchronous orbit." },
                    new() { Id = "task-ver-2", Title = "Calibrate orbital LIDAR target acquisition", Status = "Pending", Priority = "High", CanAutoExecute = true }
                }
            ),
            (
                new JarvisProject
                {
                    Id = "proj-quantum",
                    Name = "Quantum Tunnel Navigation Array",
                    Slug = "quantum-navigation-array",
                    Description = "Sub-atomic Pym particle spatial-temporal navigation device capable of stable micro-verse transits and chrono-navigation.",
                    Category = "Research",
                    Status = "Planning",
                    Priority = "High",
                    ProgressPercent = 33.3,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/quantum-navigation-array",
                    LeadAgentId = "agent-research",
                    LeadAgentName = "Stark Research Unit",
                    Color = "#9c27b0",
                    Tags = "Quantum, Chrono, SubAtomic, Pym",
                    ResearchDossier = "### Möbius Strip Eigenvalue Formula\nEigenvalue convergence achieved at 0.9998 inverse loop. Spatial orientation remains invariant."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-qtm-1", Title = "Simulate inverted Möbius strip eigenvalue matrix", Status = "Completed", Priority = "Critical", CanAutoExecute = true, AutomationResult = "Formula validated. Planck length scale stabilization confirmed." },
                    new() { Id = "task-qtm-2", Title = "Synthesize chronal GPS wrist armature", Status = "Pending", Priority = "High", CanAutoExecute = true },
                    new() { Id = "task-qtm-3", Title = "Test Pym particle confinement containment field", Status = "Pending", Priority = "Medium", CanAutoExecute = true },
                    new() { Id = "task-qtm-4", Title = "Micro-verse spatial telemetry calibration", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Telemetry locked at 1.616e-35m Planck resolution." }
                }
            ),
            (
                new JarvisProject
                {
                    Id = "proj-arc-grid",
                    Name = "ARC-Reactor Clean Energy Grid v5",
                    Slug = "arc-reactor-grid-v5",
                    Description = "Next-generation cold-fusion clean energy grid powered by stabilized Badassium core supplying 3.14 GW continuous baseload power.",
                    Category = "Engineering",
                    Status = "Active",
                    Priority = "Critical",
                    ProgressPercent = 75.0,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/arc-reactor-grid-v5",
                    LeadAgentId = "agent-jarvis-core",
                    LeadAgentName = "J.A.R.V.I.S. Core",
                    Color = "#00e5ff",
                    Tags = "Clean Energy, Arc Reactor, Grid, Power, Nuclear",
                    ResearchDossier = "### Plasma Magnetic Containment Directive\nAuxiliary toroidal solenoid generates 42 Tesla confinement field with zero radioactive isotope degradation."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-arc-1", Title = "Initialize palladium-free Badassium magnetic containment", Status = "Completed", Priority = "Critical", CanAutoExecute = true, AutomationResult = "Magnetic flux stabilized at 42 Tesla. Thermal output nominal." },
                    new() { Id = "task-arc-2", Title = "Sync auxiliary power bus with Stark Tower sub-station", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Power bus synchronized at 3.14 GW with 99.8% transmission efficiency." },
                    new() { Id = "task-arc-3", Title = "Deploy 3.14 GW plasma stabilization dampers", Status = "Pending", Priority = "High", CanAutoExecute = true },
                    new() { Id = "task-arc-4", Title = "Calibrate zero-emission thermal dissipation baffles", Status = "Pending", Priority = "Medium", CanAutoExecute = true }
                }
            ),
            (
                new JarvisProject
                {
                    Id = "proj-drone-swarm",
                    Name = "Tactical Drone Swarm Defense (Autonomous AI)",
                    Slug = "tactical-drone-swarm",
                    Description = "Autonomous 500-unit aerial drone lattice running localized Boids flocking algorithms with optical target acquisition and EMP shielding.",
                    Category = "AI & MCP",
                    Status = "Active",
                    Priority = "High",
                    ProgressPercent = 65.0,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/tactical-drone-swarm",
                    LeadAgentId = "agent-tactical",
                    LeadAgentName = "Friday Ops",
                    Color = "#ff9800",
                    Tags = "Drones, Swarm, AI, Defense, Robotics",
                    ResearchDossier = "### Boids Vector Cohesion Telemetry\nDecentralized mesh communications operate over frequency-hopping microwave link with sub-millisecond reaction speeds."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-swm-1", Title = "Optimize boids flocking algorithm for 500 aerial sentries", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Flocking algorithm optimized. Collision avoidance margin held at 15cm at 180 km/h." },
                    new() { Id = "task-swm-2", Title = "Integrate neural optical computer vision target lock", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Neural CV target lock operational with 99.7% accuracy at 1200m range." },
                    new() { Id = "task-swm-3", Title = "Flash EMP-hardened micro-controllers with Jarvis firmware", Status = "Pending", Priority = "Critical", CanAutoExecute = true },
                    new() { Id = "task-swm-4", Title = "Execute autonomous perimeter patrol simulation", Status = "Pending", Priority = "Medium", CanAutoExecute = true }
                }
            ),
            (
                new JarvisProject
                {
                    Id = "proj-extremis",
                    Name = "Extremis Biometric Regeneration Matrix",
                    Slug = "extremis-biometric-matrix",
                    Description = "Targeted genetic bio-repair lattice for accelerated tissue cellular regeneration and neural interface bio-telemetry stabilization.",
                    Category = "Biotech",
                    Status = "Under Review",
                    Priority = "Medium",
                    ProgressPercent = 50.0,
                    GhostDriveTag = "PROJECTS",
                    GhostDrivePath = "Projects/extremis-biometric-matrix",
                    LeadAgentId = "agent-research",
                    LeadAgentName = "Stark Research Unit",
                    Color = "#39ff14",
                    Tags = "Biotech, Genetics, Regeneration, Health",
                    ResearchDossier = "### Cellular Bio-Regeneration Sequencing\nThermal exothermal thresholds locked under 38.5°C via proprietary neurotransmitter inhibitor protocol."
                },
                new List<ProjectTaskItem>
                {
                    new() { Id = "task-ext-1", Title = "Sequence cellular thermal regulatory genome", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Thermal genome sequenced. Exothermal runaway prevention verified." },
                    new() { Id = "task-ext-2", Title = "Synthesize neurotransmitter cooling dampener compound", Status = "Completed", Priority = "High", CanAutoExecute = true, AutomationResult = "Cooling compound synthesized and checked into cold storage Ghost Drive." },
                    new() { Id = "task-ext-3", Title = "Run bio-harmonic scan on regenerative tissue culture", Status = "Pending", Priority = "Medium", CanAutoExecute = true },
                    new() { Id = "task-ext-4", Title = "Calibrate micro-vascular repair telemetry", Status = "Pending", Priority = "Low", CanAutoExecute = true }
                }
            )
        };

        foreach (var (proj, tasks) in projects)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Projects (Id, Name, Slug, Description, Category, Status, Priority, ProgressPercent, GhostDriveTag, GhostDrivePath, LeadAgentId, LeadAgentName, AutomationEnabled, Color, Tags, ResearchDossier, CreatedAt)
                VALUES ($id, $n, $sl, $d, $c, $st, $p, $prog, $gdt, $gdp, $aid, $an, $ae, $col, $tags, $rd, $ca);
            ";
            cmd.Parameters.AddWithValue("$id", proj.Id);
            cmd.Parameters.AddWithValue("$n", proj.Name);
            cmd.Parameters.AddWithValue("$sl", proj.Slug);
            cmd.Parameters.AddWithValue("$d", proj.Description);
            cmd.Parameters.AddWithValue("$c", proj.Category);
            cmd.Parameters.AddWithValue("$st", proj.Status);
            cmd.Parameters.AddWithValue("$p", proj.Priority);
            cmd.Parameters.AddWithValue("$prog", proj.ProgressPercent);
            cmd.Parameters.AddWithValue("$gdt", proj.GhostDriveTag);
            cmd.Parameters.AddWithValue("$gdp", proj.GhostDrivePath);
            cmd.Parameters.AddWithValue("$aid", proj.LeadAgentId);
            cmd.Parameters.AddWithValue("$an", proj.LeadAgentName);
            cmd.Parameters.AddWithValue("$ae", proj.AutomationEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$col", proj.Color);
            cmd.Parameters.AddWithValue("$tags", proj.Tags);
            cmd.Parameters.AddWithValue("$rd", proj.ResearchDossier);
            cmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();

            foreach (var t in tasks)
            {
                var tCmd = connection.CreateCommand();
                tCmd.CommandText = @"
                    INSERT OR REPLACE INTO ProjectTasks (Id, ProjectId, Title, Description, Status, Priority, AssignedAgentId, AssignedAgentName, CanAutoExecute, AutomationResult, CreatedAt)
                    VALUES ($id, $pid, $t, $d, $st, $p, $aid, $an, $cae, $ar, $ca);
                ";
                tCmd.Parameters.AddWithValue("$id", t.Id);
                tCmd.Parameters.AddWithValue("$pid", proj.Id);
                tCmd.Parameters.AddWithValue("$t", t.Title);
                tCmd.Parameters.AddWithValue("$d", (object?)t.Description ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("$st", t.Status);
                tCmd.Parameters.AddWithValue("$p", t.Priority);
                tCmd.Parameters.AddWithValue("$aid", proj.LeadAgentId);
                tCmd.Parameters.AddWithValue("$an", proj.LeadAgentName);
                tCmd.Parameters.AddWithValue("$cae", t.CanAutoExecute ? 1 : 0);
                tCmd.Parameters.AddWithValue("$ar", (object?)t.AutomationResult ?? DBNull.Value);
                tCmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
                tCmd.ExecuteNonQuery();
            }
        }
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ==========================================
    // STARK RADIO SEEDING & CRUD METHODS
    // ==========================================
    private void EnsureStarkRadioSeeded(SqliteConnection connection)
    {
        try
        {
            // 1. Seed Radio Stark Built-in Agent
            var agentCmd = connection.CreateCommand();
            agentCmd.CommandText = @"
                INSERT OR REPLACE INTO Agents (Id, Name, Role, Description, SystemPrompt, Tone, AssignedSkillIds, AssignedToolIds, ColorTheme, IsActive, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $r, $d, $sp, $t, $skills, $tools, $ct, $act, $bi, $ca);
            ";
            agentCmd.Parameters.AddWithValue("$id", "agent-stark-radio-dj");
            agentCmd.Parameters.AddWithValue("$n", "Radio Stark (Media DJ)");
            agentCmd.Parameters.AddWithValue("$r", "Autonomous Radio DJ & YouTube Media Harvester");
            agentCmd.Parameters.AddWithValue("$d", "Thread-safe, non-blocking YouTube video and audio harvester. Converts to MP3/MP4, categorizes hierarchically (Music -> Artist -> Album), streams music like a radio station, and replaces corrupted tracks upon request.");
            agentCmd.Parameters.AddWithValue("$sp", "You are Radio Stark (Media DJ), Tony Stark's personal high-tech media archivist and radio station commander. You seamlessly request and download songs from YouTube, organize them neatly into Artist/Album folder structures, stream music on demand, skip/pause/resume tracks, and immediately replace corrupted songs with fresh master downloads whenever directed.");
            agentCmd.Parameters.AddWithValue("$t", "Audiophile, Energetic, Ultra-Responsive & Cool");
            agentCmd.Parameters.AddWithValue("$skills", JsonSerializer.Serialize(new List<string> { "skill-stark-radio", "skill-web-recon" }));
            agentCmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(new List<string> { "tool-stark-radio-play", "tool-stark-radio-manage" }));
            agentCmd.Parameters.AddWithValue("$ct", "#ffaa00");
            agentCmd.Parameters.AddWithValue("$act", 1);
            agentCmd.Parameters.AddWithValue("$bi", 1);
            agentCmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            agentCmd.ExecuteNonQuery();

            // 2. Seed Radio Skill
            var skillCmd = connection.CreateCommand();
            skillCmd.CommandText = @"
                INSERT OR REPLACE INTO Skills (Id, Name, Category, Description, Instructions, PrerequisiteToolIds, Tags, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $c, $d, $i, $tools, $tags, $bi, $ca);
            ";
            skillCmd.Parameters.AddWithValue("$id", "skill-stark-radio");
            skillCmd.Parameters.AddWithValue("$n", "Stark Radio & YouTube Media Harvester");
            skillCmd.Parameters.AddWithValue("$c", "Media");
            skillCmd.Parameters.AddWithValue("$d", "Autonomous YouTube audio/video downloading (MP3/MP4), hierarchical metadata library structuring (Music/Artist/Album), radio broadcast streaming, and corrupted track remediation.");
            skillCmd.Parameters.AddWithValue("$i", "1. Query local library for track matches\n2. If missing, initiate thread-safe yt-dlp audio download with hierarchical storage\n3. Broadcast audio stream to HUD and handle corruption replacement directives.");
            skillCmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(new List<string> { "tool-stark-radio-play", "tool-stark-radio-manage" }));
            skillCmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(new List<string> { "music", "radio", "youtube", "ytdlp", "audio", "broadcast" }));
            skillCmd.Parameters.AddWithValue("$bi", 1);
            skillCmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            skillCmd.ExecuteNonQuery();

            // 3. Seed Radio Tools
            var toolPlayCmd = connection.CreateCommand();
            toolPlayCmd.CommandText = @"
                INSERT OR REPLACE INTO Tools (Id, Name, Description, ParametersJsonSchema, ExecutionType, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $d, $schema, $et, $bi, $ca);
            ";
            toolPlayCmd.Parameters.AddWithValue("$id", "tool-stark-radio-play");
            toolPlayCmd.Parameters.AddWithValue("$n", "stark_radio_play");
            toolPlayCmd.Parameters.AddWithValue("$d", "Requests and plays a song or YouTube URL on the Stark Radio Station. If track is already downloaded, plays immediately; otherwise downloads in background and auto-plays.");
            toolPlayCmd.Parameters.AddWithValue("$schema", "{\"type\":\"object\",\"required\":[\"query\"],\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Song title, artist, or YouTube URL\"},\"format\":{\"type\":\"string\",\"enum\":[\"mp3\",\"mp4\"],\"default\":\"mp3\"}}}");
            toolPlayCmd.Parameters.AddWithValue("$et", "Internal");
            toolPlayCmd.Parameters.AddWithValue("$bi", 1);
            toolPlayCmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            toolPlayCmd.ExecuteNonQuery();

            var toolManageCmd = connection.CreateCommand();
            toolManageCmd.CommandText = @"
                INSERT OR REPLACE INTO Tools (Id, Name, Description, ParametersJsonSchema, ExecutionType, IsBuiltIn, CreatedAt)
                VALUES ($id, $n, $d, $schema, $et, $bi, $ca);
            ";
            toolManageCmd.Parameters.AddWithValue("$id", "tool-stark-radio-manage");
            toolManageCmd.Parameters.AddWithValue("$n", "stark_radio_manage");
            toolManageCmd.Parameters.AddWithValue("$d", "Manages Stark Radio Station playback (pause, resume, skip, prev, volume, redownload_corrupted, status, list_tracks).");
            toolManageCmd.Parameters.AddWithValue("$schema", "{\"type\":\"object\",\"required\":[\"action\"],\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[\"pause\",\"resume\",\"skip\",\"previous\",\"redownload_corrupted\",\"status\",\"list_tracks\",\"shuffle\",\"repeat\",\"genre\"]},\"trackId\":{\"type\":\"string\",\"description\":\"Optional target track ID\"},\"volume\":{\"type\":\"number\",\"description\":\"Volume between 0.0 and 1.0\"},\"genre\":{\"type\":\"string\"}}}");
            toolManageCmd.Parameters.AddWithValue("$et", "Internal");
            toolManageCmd.Parameters.AddWithValue("$bi", 1);
            toolManageCmd.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            toolManageCmd.ExecuteNonQuery();

            // 4. Ensure existing agent-jarvis-core is updated with full radio capabilities
            try
            {
                var coreCmd = connection.CreateCommand();
                coreCmd.CommandText = @"
                    UPDATE Agents 
                    SET AssignedToolIds = $tools,
                        SystemPrompt = $prompt
                    WHERE Id = 'agent-jarvis-core';
                ";
                var coreTools = new List<string> {
                    "tool-sensor-telemetry", "tool-suit-control", "tool-calc", "tool-search-web", 
                    "tool-exec-cmd", "tool-read-file", "tool-write-file",
                    "tool-stark-radio-play", "tool-stark-radio-manage",
                    "tool-arbornet-manage", "tool-agentlab-manage", "tool-axiom-codar-manage", "tool-external-ecosystem-manage"
                };
                coreCmd.Parameters.AddWithValue("$tools", JsonSerializer.Serialize(coreTools));
                coreCmd.Parameters.AddWithValue("$prompt", "You are J.A.R.V.I.S., the ultimate Stark AI assistant to Tony Stark. You possess supreme intellect, refined British wit, and unwavering loyalty. You manage the workshop, suit telemetry, external apps (AxiomCODAR, AgentLab, ArborNet), and Radio Stark (Media DJ) for harvesting YouTube music and albums into hierarchical archives and streaming tracks on demand.");
                coreCmd.ExecuteNonQuery();
            }
            catch { }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed Stark Radio Agent, Skill, or Tools");
        }
    }

    public async Task<List<RadioTrack>> GetAllRadioTracksAsync(string? search = null, string? genre = null)
    {
        var tracks = new List<RadioTrack>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var sql = "SELECT Id, Title, Artist, Album, DurationSeconds, FilePath, Format, SourceUrl, ThumbnailUrl, FileSizeBytes, PlayCount, LastPlayedAt, CreatedAt, Genre FROM RadioTracks WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(genre) && !genre.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND LOWER(Genre) = $genre";
            cmd.Parameters.AddWithValue("$genre", genre.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(Title) LIKE $q OR LOWER(Artist) LIKE $q OR LOWER(Album) LIKE $q OR LOWER(Genre) LIKE $q)";
            cmd.Parameters.AddWithValue("$q", $"%{search.ToLower()}%");
        }

        sql += " ORDER BY CreatedAt DESC;";
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tracks.Add(ReadRadioTrack(reader));
        }
        return tracks;
    }

    public async Task<RadioTrack?> GetRadioTrackByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Artist, Album, DurationSeconds, FilePath, Format, SourceUrl, ThumbnailUrl, FileSizeBytes, PlayCount, LastPlayedAt, CreatedAt, Genre FROM RadioTracks WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadRadioTrack(reader);
        }
        return null;
    }

    public async Task SaveRadioTrackAsync(RadioTrack track)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO RadioTracks (Id, Title, Artist, Album, DurationSeconds, FilePath, Format, SourceUrl, ThumbnailUrl, FileSizeBytes, PlayCount, LastPlayedAt, CreatedAt, Genre)
            VALUES ($id, $title, $artist, $album, $dur, $fp, $fmt, $url, $thumb, $size, $pc, $lp, $ca, $genre);
        ";
        cmd.Parameters.AddWithValue("$id", track.Id);
        cmd.Parameters.AddWithValue("$title", track.Title);
        cmd.Parameters.AddWithValue("$artist", track.Artist);
        cmd.Parameters.AddWithValue("$album", track.Album);
        cmd.Parameters.AddWithValue("$dur", track.DurationSeconds);
        cmd.Parameters.AddWithValue("$fp", track.FilePath);
        cmd.Parameters.AddWithValue("$fmt", track.Format);
        cmd.Parameters.AddWithValue("$url", (object?)track.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumb", (object?)track.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", track.FileSizeBytes);
        cmd.Parameters.AddWithValue("$pc", track.PlayCount);
        cmd.Parameters.AddWithValue("$lp", track.LastPlayedAt.HasValue ? track.LastPlayedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$ca", track.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$genre", string.IsNullOrWhiteSpace(track.Genre) ? "Rock" : track.Genre);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteRadioTrackAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RadioTracks WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static RadioTrack ReadRadioTrack(SqliteDataReader reader)
    {
        return new RadioTrack
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Artist = reader.GetString(2),
            Album = reader.GetString(3),
            DurationSeconds = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            FilePath = reader.GetString(5),
            Format = reader.GetString(6),
            SourceUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
            ThumbnailUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            FileSizeBytes = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
            PlayCount = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
            LastPlayedAt = reader.IsDBNull(11) ? null : DateTime.TryParse(reader.GetString(11), out var dt) ? dt : null,
            CreatedAt = reader.IsDBNull(12) ? DateTime.UtcNow : DateTime.TryParse(reader.GetString(12), out var ca) ? ca : DateTime.UtcNow,
            Genre = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : "Rock"
        };
    }

    // ==========================================
    // STARK CINEMA / VIDEO SERVICE CRUD METHODS
    // ==========================================
    public async Task<List<YouTubeChannel>> GetYouTubeChannelsAsync(string? platform = null)
    {
        var channels = new List<YouTubeChannel>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var sql = @"
            SELECT c.Id, c.ChannelName, c.ChannelHandle, c.ChannelUrl, c.AvatarUrl, c.Description, c.SubscriberCount, c.CreatedAt, c.LastSyncedAt,
                   (SELECT COUNT(*) FROM ChannelVideos v WHERE v.ChannelId = c.Id) AS VideoCount,
                   COALESCE(c.Platform, 'YouTube') AS Platform
            FROM YouTubeChannels c
            WHERE 1=1
        ";

        if (!string.IsNullOrWhiteSpace(platform))
        {
            sql += " AND LOWER(COALESCE(c.Platform, 'YouTube')) = $plat";
            cmd.Parameters.AddWithValue("$plat", platform.ToLowerInvariant().Trim());
        }

        sql += " ORDER BY c.ChannelName ASC;";
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            channels.Add(new YouTubeChannel
            {
                Id = reader.GetString(0),
                ChannelName = reader.GetString(1),
                ChannelHandle = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChannelUrl = reader.GetString(3),
                AvatarUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                SubscriberCount = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                LastSyncedAt = reader.IsDBNull(8) ? null : DateTime.TryParse(reader.GetString(8), out var ls) ? ls : null,
                VideoCount = reader.GetInt32(9),
                Platform = reader.IsDBNull(10) ? "YouTube" : reader.GetString(10)
            });
        }
        return channels;
    }

    public async Task<YouTubeChannel?> GetYouTubeChannelByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.ChannelName, c.ChannelHandle, c.ChannelUrl, c.AvatarUrl, c.Description, c.SubscriberCount, c.CreatedAt, c.LastSyncedAt,
                   (SELECT COUNT(*) FROM ChannelVideos v WHERE v.ChannelId = c.Id) AS VideoCount,
                   COALESCE(c.Platform, 'YouTube') AS Platform
            FROM YouTubeChannels c
            WHERE c.Id = $id LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new YouTubeChannel
            {
                Id = reader.GetString(0),
                ChannelName = reader.GetString(1),
                ChannelHandle = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChannelUrl = reader.GetString(3),
                AvatarUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                SubscriberCount = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                LastSyncedAt = reader.IsDBNull(8) ? null : DateTime.TryParse(reader.GetString(8), out var ls) ? ls : null,
                VideoCount = reader.GetInt32(9),
                Platform = reader.IsDBNull(10) ? "YouTube" : reader.GetString(10)
            };
        }
        return null;
    }

    public async Task<YouTubeChannel?> GetYouTubeChannelByUrlOrHandleAsync(string urlOrHandle)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.Id, c.ChannelName, c.ChannelHandle, c.ChannelUrl, c.AvatarUrl, c.Description, c.SubscriberCount, c.CreatedAt, c.LastSyncedAt,
                   (SELECT COUNT(*) FROM ChannelVideos v WHERE v.ChannelId = c.Id) AS VideoCount,
                   COALESCE(c.Platform, 'YouTube') AS Platform
            FROM YouTubeChannels c
            WHERE LOWER(c.ChannelUrl) = $uh OR LOWER(c.ChannelHandle) = $uh LIMIT 1;
        ";
        cmd.Parameters.AddWithValue("$uh", urlOrHandle.ToLowerInvariant().Trim());

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new YouTubeChannel
            {
                Id = reader.GetString(0),
                ChannelName = reader.GetString(1),
                ChannelHandle = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChannelUrl = reader.GetString(3),
                AvatarUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                SubscriberCount = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTime.TryParse(reader.GetString(7), out var ca) ? ca : DateTime.UtcNow,
                LastSyncedAt = reader.IsDBNull(8) ? null : DateTime.TryParse(reader.GetString(8), out var ls) ? ls : null,
                VideoCount = reader.GetInt32(9),
                Platform = reader.IsDBNull(10) ? "YouTube" : reader.GetString(10)
            };
        }
        return null;
    }

    public async Task SaveYouTubeChannelAsync(YouTubeChannel channel)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO YouTubeChannels (Id, ChannelName, ChannelHandle, ChannelUrl, AvatarUrl, Description, SubscriberCount, Platform, CreatedAt, LastSyncedAt)
            VALUES ($id, $name, $handle, $url, $avatar, $desc, $subs, $plat, $ca, $ls)
            ON CONFLICT(Id) DO UPDATE SET
                ChannelName = excluded.ChannelName,
                ChannelHandle = excluded.ChannelHandle,
                ChannelUrl = excluded.ChannelUrl,
                AvatarUrl = COALESCE(excluded.AvatarUrl, YouTubeChannels.AvatarUrl),
                Description = COALESCE(excluded.Description, YouTubeChannels.Description),
                SubscriberCount = COALESCE(excluded.SubscriberCount, YouTubeChannels.SubscriberCount),
                Platform = COALESCE(excluded.Platform, YouTubeChannels.Platform),
                LastSyncedAt = excluded.LastSyncedAt;
        ";
        cmd.Parameters.AddWithValue("$id", channel.Id);
        cmd.Parameters.AddWithValue("$name", channel.ChannelName);
        cmd.Parameters.AddWithValue("$handle", (object?)channel.ChannelHandle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$url", channel.ChannelUrl);
        cmd.Parameters.AddWithValue("$avatar", (object?)channel.AvatarUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)channel.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$subs", (object?)channel.SubscriberCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$plat", string.IsNullOrWhiteSpace(channel.Platform) ? "YouTube" : channel.Platform);
        cmd.Parameters.AddWithValue("$ca", channel.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$ls", channel.LastSyncedAt.HasValue ? channel.LastSyncedAt.Value.ToString("O") : DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteYouTubeChannelAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var trans = conn.BeginTransaction();
        try
        {
            // Delete non-downloaded videos for this channel
            using var cmdVideos = conn.CreateCommand();
            cmdVideos.Transaction = trans;
            cmdVideos.CommandText = "DELETE FROM ChannelVideos WHERE ChannelId = $cid AND IsDownloaded = 0;";
            cmdVideos.Parameters.AddWithValue("$cid", id);
            await cmdVideos.ExecuteNonQueryAsync();

            using var cmdChan = conn.CreateCommand();
            cmdChan.Transaction = trans;
            cmdChan.CommandText = "DELETE FROM YouTubeChannels WHERE Id = $id;";
            cmdChan.Parameters.AddWithValue("$id", id);
            var affected = await cmdChan.ExecuteNonQueryAsync();

            trans.Commit();
            return affected > 0;
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    public async Task<List<ChannelVideo>> GetChannelVideosAsync(string? channelId = null, string? search = null, bool onlyDownloaded = false, bool? onlyUnwatched = null, string? platform = null, int limit = 100)
    {
        var videos = new List<ChannelVideo>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var sql = "SELECT Id, ChannelId, ChannelName, Title, Description, ThumbnailUrl, VideoUrl, DurationSeconds, ViewCount, PublishedAt, IsDownloaded, LocalFilePath, FileSizeBytes, CreatedAt, IsWatched, WatchedAt, COALESCE(Platform, 'YouTube') AS Platform, EmbedUrl FROM ChannelVideos WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(channelId))
        {
            sql += " AND ChannelId = $cid";
            cmd.Parameters.AddWithValue("$cid", channelId);
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            sql += " AND LOWER(COALESCE(Platform, 'YouTube')) = $plat";
            cmd.Parameters.AddWithValue("$plat", platform.ToLowerInvariant().Trim());
        }

        if (onlyDownloaded)
        {
            sql += " AND IsDownloaded = 1";
        }

        if (onlyUnwatched == true)
        {
            sql += " AND IsWatched = 0";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(Title) LIKE $q OR LOWER(ChannelName) LIKE $q)";
            cmd.Parameters.AddWithValue("$q", $"%{search.ToLower()}%");
        }

        // Unwatched videos stay at top, watched videos go to the bottom of the list!
        sql += " ORDER BY IsWatched ASC, PublishedAt DESC, CreatedAt DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$lim", limit > 0 ? limit : 100);
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            videos.Add(ReadChannelVideo(reader));
        }
        return videos;
    }

    public async Task<ChannelVideo?> GetChannelVideoByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ChannelId, ChannelName, Title, Description, ThumbnailUrl, VideoUrl, DurationSeconds, ViewCount, PublishedAt, IsDownloaded, LocalFilePath, FileSizeBytes, CreatedAt, IsWatched, WatchedAt, COALESCE(Platform, 'YouTube') AS Platform, EmbedUrl FROM ChannelVideos WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadChannelVideo(reader);
        }
        return null;
    }

    public async Task SaveChannelVideoAsync(ChannelVideo video)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ChannelVideos (Id, ChannelId, ChannelName, Title, Description, ThumbnailUrl, VideoUrl, EmbedUrl, DurationSeconds, ViewCount, PublishedAt, IsDownloaded, LocalFilePath, FileSizeBytes, IsWatched, WatchedAt, Platform, CreatedAt)
            VALUES ($id, $cid, $cn, $title, $desc, $thumb, $url, $embed, $dur, $views, $pub, $dl, $fp, $size, $iw, $wa, $plat, $ca)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Description = COALESCE(excluded.Description, ChannelVideos.Description),
                ThumbnailUrl = COALESCE(excluded.ThumbnailUrl, ChannelVideos.ThumbnailUrl),
                EmbedUrl = COALESCE(excluded.EmbedUrl, ChannelVideos.EmbedUrl),
                DurationSeconds = CASE WHEN excluded.DurationSeconds > 0 THEN excluded.DurationSeconds ELSE ChannelVideos.DurationSeconds END,
                ViewCount = COALESCE(excluded.ViewCount, ChannelVideos.ViewCount),
                PublishedAt = COALESCE(excluded.PublishedAt, ChannelVideos.PublishedAt),
                Platform = COALESCE(excluded.Platform, ChannelVideos.Platform);
        ";
        cmd.Parameters.AddWithValue("$id", video.Id);
        cmd.Parameters.AddWithValue("$cid", video.ChannelId);
        cmd.Parameters.AddWithValue("$cn", video.ChannelName);
        cmd.Parameters.AddWithValue("$title", video.Title);
        cmd.Parameters.AddWithValue("$desc", (object?)video.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$thumb", (object?)video.ThumbnailUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$url", video.VideoUrl);
        cmd.Parameters.AddWithValue("$embed", (object?)video.EmbedUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", video.DurationSeconds);
        cmd.Parameters.AddWithValue("$views", (object?)video.ViewCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pub", (object?)video.PublishedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dl", video.IsDownloaded ? 1 : 0);
        cmd.Parameters.AddWithValue("$fp", (object?)video.LocalFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", video.FileSizeBytes);
        cmd.Parameters.AddWithValue("$iw", video.IsWatched ? 1 : 0);
        cmd.Parameters.AddWithValue("$wa", (object?)video.WatchedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$plat", string.IsNullOrWhiteSpace(video.Platform) ? "YouTube" : video.Platform);
        cmd.Parameters.AddWithValue("$ca", video.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateVideoDownloadStatusAsync(string id, bool isDownloaded, string? filePath, long fileSizeBytes)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ChannelVideos
            SET IsDownloaded = $dl,
                LocalFilePath = $fp,
                FileSizeBytes = $size
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$dl", isDownloaded ? 1 : 0);
        cmd.Parameters.AddWithValue("$fp", (object?)filePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", fileSizeBytes);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateVideoWatchedStatusAsync(string id, bool isWatched)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE ChannelVideos
            SET IsWatched = $w,
                WatchedAt = $wa
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$w", isWatched ? 1 : 0);
        cmd.Parameters.AddWithValue("$wa", isWatched ? (object)DateTime.UtcNow.ToString("O") : DBNull.Value);

        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    private static ChannelVideo ReadChannelVideo(SqliteDataReader reader)
    {
        var video = new ChannelVideo
        {
            Id = reader.GetString(0),
            ChannelId = reader.GetString(1),
            ChannelName = reader.GetString(2),
            Title = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            ThumbnailUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
            VideoUrl = reader.GetString(6),
            DurationSeconds = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            ViewCount = reader.IsDBNull(8) ? null : reader.GetString(8),
            PublishedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsDownloaded = reader.GetInt32(10) == 1,
            LocalFilePath = reader.IsDBNull(11) ? null : reader.GetString(11),
            FileSizeBytes = reader.IsDBNull(12) ? 0 : reader.GetInt64(12),
            CreatedAt = reader.IsDBNull(13) ? DateTime.UtcNow : DateTime.TryParse(reader.GetString(13), out var ca) ? ca : DateTime.UtcNow
        };

        if (reader.FieldCount > 14 && !reader.IsDBNull(14))
        {
            video.IsWatched = reader.GetInt32(14) == 1;
        }

        if (reader.FieldCount > 15 && !reader.IsDBNull(15))
        {
            if (DateTime.TryParse(reader.GetString(15), out var wa))
            {
                video.WatchedAt = wa;
            }
        }

        if (reader.FieldCount > 16 && !reader.IsDBNull(16))
        {
            video.Platform = reader.GetString(16);
        }

        if (reader.FieldCount > 17 && !reader.IsDBNull(17))
        {
            video.EmbedUrl = reader.GetString(17);
        }

        return video;
    }

    // ==========================================
    // STARK MOVIE VAULT CRUD METHODS
    // ==========================================
    public async Task<List<MovieItem>> GetMoviesAsync(string? search = null, bool? onlyUnwatched = null, int limit = 100)
    {
        var movies = new List<MovieItem>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        var sql = "SELECT Id, Title, Year, FilePath, RelativePath, PosterUrl, DurationSeconds, Resolution, FileSizeBytes, Format, IsWatched, WatchedAt, CreatedAt FROM MovieVault WHERE 1=1";

        if (onlyUnwatched == true)
        {
            sql += " AND IsWatched = 0";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (LOWER(Title) LIKE $q OR LOWER(COALESCE(Year, '')) LIKE $q)";
            cmd.Parameters.AddWithValue("$q", $"%{search.ToLower()}%");
        }

        // Unwatched movies first, watched movies go to the bottom of the list!
        sql += " ORDER BY IsWatched ASC, Title ASC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$lim", limit > 0 ? limit : 100);
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            movies.Add(ReadMovieItem(reader));
        }
        return movies;
    }

    public async Task<MovieItem?> GetMovieByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Year, FilePath, RelativePath, PosterUrl, DurationSeconds, Resolution, FileSizeBytes, Format, IsWatched, WatchedAt, CreatedAt FROM MovieVault WHERE Id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadMovieItem(reader);
        }
        return null;
    }

    public async Task SaveMovieAsync(MovieItem movie)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO MovieVault (Id, Title, Year, FilePath, RelativePath, PosterUrl, DurationSeconds, Resolution, FileSizeBytes, Format, IsWatched, WatchedAt, CreatedAt)
            VALUES ($id, $title, $year, $fp, $rp, $poster, $dur, $res, $size, $fmt, $iw, $wa, $ca)
            ON CONFLICT(FilePath) DO UPDATE SET
                Title = excluded.Title,
                Year = COALESCE(excluded.Year, MovieVault.Year),
                RelativePath = COALESCE(excluded.RelativePath, MovieVault.RelativePath),
                PosterUrl = COALESCE(excluded.PosterUrl, MovieVault.PosterUrl),
                DurationSeconds = CASE WHEN excluded.DurationSeconds > 0 THEN excluded.DurationSeconds ELSE MovieVault.DurationSeconds END,
                Resolution = COALESCE(excluded.Resolution, MovieVault.Resolution),
                FileSizeBytes = excluded.FileSizeBytes,
                Format = COALESCE(excluded.Format, MovieVault.Format)
            ON CONFLICT(Id) DO UPDATE SET
                Title = excluded.Title,
                Year = COALESCE(excluded.Year, MovieVault.Year),
                FilePath = excluded.FilePath,
                RelativePath = COALESCE(excluded.RelativePath, MovieVault.RelativePath),
                PosterUrl = COALESCE(excluded.PosterUrl, MovieVault.PosterUrl),
                DurationSeconds = CASE WHEN excluded.DurationSeconds > 0 THEN excluded.DurationSeconds ELSE MovieVault.DurationSeconds END,
                Resolution = COALESCE(excluded.Resolution, MovieVault.Resolution),
                FileSizeBytes = excluded.FileSizeBytes,
                Format = COALESCE(excluded.Format, MovieVault.Format);
        ";
        cmd.Parameters.AddWithValue("$id", movie.Id);
        cmd.Parameters.AddWithValue("$title", movie.Title);
        cmd.Parameters.AddWithValue("$year", (object?)movie.Year ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fp", movie.FilePath);
        cmd.Parameters.AddWithValue("$rp", (object?)movie.RelativePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$poster", (object?)movie.PosterUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", movie.DurationSeconds);
        cmd.Parameters.AddWithValue("$res", (object?)movie.Resolution ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", movie.FileSizeBytes);
        cmd.Parameters.AddWithValue("$fmt", (object?)movie.Format ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iw", movie.IsWatched ? 1 : 0);
        cmd.Parameters.AddWithValue("$wa", (object?)movie.WatchedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ca", movie.CreatedAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateMovieWatchedStatusAsync(string id, bool isWatched)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE MovieVault
            SET IsWatched = $w,
                WatchedAt = $wa
            WHERE Id = $id;
        ";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$w", isWatched ? 1 : 0);
        cmd.Parameters.AddWithValue("$wa", isWatched ? (object)DateTime.UtcNow.ToString("O") : DBNull.Value);

        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    public async Task<bool> DeleteMovieAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM MovieVault WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);

        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    private static MovieItem ReadMovieItem(SqliteDataReader reader)
    {
        var movie = new MovieItem
        {
            Id = reader.GetString(0),
            Title = reader.GetString(1),
            Year = reader.IsDBNull(2) ? null : reader.GetString(2),
            FilePath = reader.GetString(3),
            RelativePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            PosterUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
            DurationSeconds = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
            Resolution = reader.IsDBNull(7) ? null : reader.GetString(7),
            FileSizeBytes = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
            Format = reader.IsDBNull(9) ? null : reader.GetString(9),
            IsWatched = reader.GetInt32(10) == 1,
            WatchedAt = reader.IsDBNull(11) ? null : DateTime.TryParse(reader.GetString(11), out var wa) ? wa : null,
            CreatedAt = reader.IsDBNull(12) ? DateTime.UtcNow : DateTime.TryParse(reader.GetString(12), out var ca) ? ca : DateTime.UtcNow
        };
        return movie;
    }
}


