using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FlexiFit.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ntr_allergies",
                columns: table => new
                {
                    allergy_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    allergy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ntr_allergies", x => x.allergy_id);
                });

            migrationBuilder.CreateTable(
                name: "ntr_food_items",
                columns: table => new
                {
                    food_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    food_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    meal_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    dietary_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "Food"),
                    size_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Regular"),
                    serving_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Serving"),
                    serving_weight_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    calories = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    protein_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    carbs_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    fats_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    img_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_food__2F4C4DD8703F737A", x => x.food_id);
                });

            migrationBuilder.CreateTable(
                name: "ntr_meal_templates",
                columns: table => new
                {
                    template_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    dietary_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_meal__BE44E0790A220F73", x => x.template_id);
                });

            migrationBuilder.CreateTable(
                name: "usr_users",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    firebase_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "USER"),
                    status = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "Active"),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    auth_provider = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__B9BE370FB3CBFE25", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "wrk_program_templates",
                columns: table => new
                {
                    program_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    program_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    fitness_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    months_per_cycle = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    weeks_per_month = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    days_per_week = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    environment = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    equipment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    session_structure = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wrk_prog__3A7890AC32B98A84", x => x.program_id);
                });

            migrationBuilder.CreateTable(
                name: "wrk_workouts",
                columns: table => new
                {
                    workout_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workout_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    muscle_group = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    equipment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    environment = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    difficulty_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_weighted = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    calories_burned = table.Column<int>(type: "integer", nullable: true, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    video_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    img_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    duration = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wrk_work__02AB2F8E16CDC0FE", x => x.workout_id);
                });

            migrationBuilder.CreateTable(
                name: "ntr_food_allergies",
                columns: table => new
                {
                    food_id = table.Column<int>(type: "integer", nullable: false),
                    allergy_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ntr_food_allergies", x => new { x.food_id, x.allergy_id });
                    table.ForeignKey(
                        name: "FK_ntr_food_allergies_ntr_allergies_allergy_id",
                        column: x => x.allergy_id,
                        principalTable: "ntr_allergies",
                        principalColumn: "allergy_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ntr_food_allergies_ntr_food_items_food_id",
                        column: x => x.food_id,
                        principalTable: "ntr_food_items",
                        principalColumn: "food_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_template_days",
                columns: table => new
                {
                    template_day_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    variation_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "A"),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_temp__F828DE9A4CE042C3", x => x.template_day_id);
                    table.ForeignKey(
                        name: "FK_ntr_template_days_template",
                        column: x => x.template_id,
                        principalTable: "ntr_meal_templates",
                        principalColumn: "template_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "act_activity_summary",
                columns: table => new
                {
                    summary_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    calories_burned = table.Column<int>(type: "integer", nullable: false),
                    total_minutes = table.Column<int>(type: "integer", nullable: false),
                    log_date = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__act_acti__85F93E83EE5828C3", x => x.summary_id);
                    table.ForeignKey(
                        name: "FK_act_activity_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_user_allergies",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    allergy_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ntr_user_allergies", x => new { x.user_id, x.allergy_id });
                    table.ForeignKey(
                        name: "FK_ntr_user_allergies_ntr_allergies_allergy_id",
                        column: x => x.allergy_id,
                        principalTable: "ntr_allergies",
                        principalColumn: "allergy_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ntr_user_allergies_usr_users_user_id",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_user_cycle_targets",
                columns: table => new
                {
                    cycle_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    weeks_in_cycle = table.Column<int>(type: "integer", nullable: false, defaultValue: 4),
                    daily_target_net_calories = table.Column<int>(type: "integer", nullable: false),
                    goal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    protein_target_g = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    carbs_target_g = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    fats_target_g = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_user__5D9558815CC833BD", x => x.cycle_id);
                    table.ForeignKey(
                        name: "FK_ntr_cycle_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_user_nutrition_profile",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    age = table.Column<int>(type: "integer", nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    nutrition_goal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    activity_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dietary_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_profile_complete = table.Column<bool>(type: "boolean", nullable: false),
                    target_weight_kg = table.Column<decimal>(type: "numeric(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_user__B9BE370FB0DF2DB0", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_ntr_profile_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_water_logs",
                columns: table => new
                {
                    water_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    water_ml = table.Column<int>(type: "integer", nullable: false),
                    log_date = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "CURRENT_DATE"),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_wate__0618B2D221BCBA83", x => x.water_log_id);
                    table.ForeignKey(
                        name: "FK_ntr_water_user_link",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_device_tokens",
                columns: table => new
                {
                    device_token_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    fcm_token = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    platform = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "android"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_devi__3ADABB7DC0BD9F4B", x => x.device_token_id);
                    table.ForeignKey(
                        name: "FK_usr_device_tokens_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_notification_history",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usr_notification_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_history_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_metrics",
                columns: table => new
                {
                    metric_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    current_weight_kg = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    current_height_cm = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    fitness_goal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Strength"),
                    nutrition_goal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Maintain"),
                    calorie_target = table.Column<int>(type: "integer", nullable: true),
                    protein_target_g = table.Column<int>(type: "integer", nullable: true),
                    carbs_target_g = table.Column<int>(type: "integer", nullable: true),
                    fats_target_g = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__13D5DCA4F6173695", x => x.metric_id);
                    table.ForeignKey(
                        name: "FK_usr_user_metrics_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_notification_settings",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    workout_reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    workout_reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    meal_reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    meal_reminder_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    water_reminder_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    water_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    water_end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    water_interval_minutes = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    daily_water_goal = table.Column<int>(type: "integer", nullable: false, defaultValue: 8),
                    glass_size_ml = table.Column<int>(type: "integer", nullable: false, defaultValue: 250),
                    calorie_display_mode = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "remaining")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__user_not__B9BE370F9E422610", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_user_notification_settings_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "usr_user_onboarding_details",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    upper_body_injury = table.Column<bool>(type: "boolean", nullable: false),
                    lower_body_injury = table.Column<bool>(type: "boolean", nullable: false),
                    joint_problems = table.Column<bool>(type: "boolean", nullable: false),
                    short_breath = table.Column<bool>(type: "boolean", nullable: false),
                    health_none = table.Column<bool>(type: "boolean", nullable: false),
                    activity_level = table.Column<string>(type: "text", nullable: true),
                    fitness_level = table.Column<string>(type: "text", nullable: true),
                    environment = table.Column<string>(type: "text", nullable: true),
                    fitness_goals = table.Column<string>(type: "text", nullable: true),
                    body_goal = table.Column<string>(type: "text", nullable: true),
                    diet_type = table.Column<string>(type: "text", nullable: true),
                    selected_programs = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usr_user_onboarding_details", x => x.id);
                    table.ForeignKey(
                        name: "FK_usr_user_onboarding_details_usr_users_user_id",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_profile_versions",
                columns: table => new
                {
                    profile_version_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    fitness_level_selected = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    goal_selected = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__87F5126507C5745C", x => x.profile_version_id);
                    table.ForeignKey(
                        name: "FK_usr_user_profile_versions_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_profiles",
                columns: table => new
                {
                    profile_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: true),
                    avatar_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__AEBB701FD9D16A8B", x => x.profile_id);
                    table.UniqueConstraint("AK_usr_user_profiles_user_id", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_usr_user_profiles_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wkt_workout_calendars",
                columns: table => new
                {
                    calendar_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    cycle_id = table.Column<int>(type: "integer", nullable: false),
                    plan_date = table.Column<DateOnly>(type: "date", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    variation_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "A"),
                    is_workout_day = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wkt_workout_calendars", x => x.calendar_id);
                    table.ForeignKey(
                        name: "FK_wkt_workout_calendars_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wrk_program_template_days",
                columns: table => new
                {
                    template_day_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    month_no = table.Column<int>(type: "integer", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    day_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wrk_prog__F828DE9A6177FA3E", x => x.template_day_id);
                    table.ForeignKey(
                        name: "FK_wrk_program_template_days_program",
                        column: x => x.program_id,
                        principalTable: "wrk_program_templates",
                        principalColumn: "program_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wrk_program_template_daytype_workouts",
                columns: table => new
                {
                    daytype_w_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    day_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    workout_id = table.Column<int>(type: "integer", nullable: false),
                    sets_default = table.Column<int>(type: "integer", nullable: false),
                    reps_default = table.Column<int>(type: "integer", nullable: false),
                    rest_seconds = table.Column<int>(type: "integer", nullable: true),
                    workout_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_primary_lift = table.Column<bool>(type: "boolean", nullable: false),
                    muscle_priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wrk_prog__C5C1481DAEF5B917", x => x.daytype_w_id);
                    table.ForeignKey(
                        name: "FK_wrk_daytype_w_program",
                        column: x => x.program_id,
                        principalTable: "wrk_program_templates",
                        principalColumn: "program_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wrk_daytype_w_workout",
                        column: x => x.workout_id,
                        principalTable: "wrk_workouts",
                        principalColumn: "workout_id");
                });

            migrationBuilder.CreateTable(
                name: "wrk_workout_load_steps",
                columns: table => new
                {
                    load_step_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    workout_id = table.Column<int>(type: "integer", nullable: false),
                    level_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    step_no = table.Column<int>(type: "integer", nullable: false),
                    load_kg = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__wrk_work__405968146EE249E6", x => x.load_step_id);
                    table.ForeignKey(
                        name: "FK_wrk_workout_load_steps_workout",
                        column: x => x.workout_id,
                        principalTable: "wrk_workouts",
                        principalColumn: "workout_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_template_day_meals",
                columns: table => new
                {
                    template_meal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_day_id = table.Column<int>(type: "integer", nullable: false),
                    meal_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_share_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_temp__B793799AAFE48243", x => x.template_meal_id);
                    table.ForeignKey(
                        name: "FK_ntr_template_day_meals_day",
                        column: x => x.template_day_id,
                        principalTable: "ntr_template_days",
                        principalColumn: "template_day_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_daily_logs",
                columns: table => new
                {
                    daily_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    cycle_id = table.Column<int>(type: "integer", nullable: false),
                    plan_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_net_calories = table.Column<int>(type: "integer", nullable: false),
                    calories_consumed = table.Column<int>(type: "integer", nullable: false),
                    calories_burned = table.Column<int>(type: "integer", nullable: false),
                    net_calories = table.Column<int>(type: "integer", nullable: true, computedColumnSql: "\"calories_consumed\" - \"calories_burned\"", stored: true),
                    goal_met = table.Column<bool>(type: "boolean", nullable: false),
                    marked_done_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_dail__419B09B5C3B21459", x => x.daily_log_id);
                    table.ForeignKey(
                        name: "FK_ntr_daily_logs_cycle",
                        column: x => x.cycle_id,
                        principalTable: "ntr_user_cycle_targets",
                        principalColumn: "cycle_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ntr_daily_logs_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "ntr_meal_plan_calendar",
                columns: table => new
                {
                    calendar_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cycle_id = table.Column<int>(type: "integer", nullable: false),
                    plan_date = table.Column<DateOnly>(type: "date", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    variation_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "A"),
                    is_workout_day = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "PENDING"),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_meal__584C1344FF6CEFBC", x => x.calendar_id);
                    table.ForeignKey(
                        name: "FK_ntr_calendar_cycle",
                        column: x => x.cycle_id,
                        principalTable: "ntr_user_cycle_targets",
                        principalColumn: "cycle_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ntr_calendar_template",
                        column: x => x.template_id,
                        principalTable: "ntr_meal_templates",
                        principalColumn: "template_id");
                });

            migrationBuilder.CreateTable(
                name: "usr_user_program_achievements",
                columns: table => new
                {
                    achievement_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    profile_version_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "COMPLETED"),
                    completed_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__3C492E83B02C8E3C", x => x.achievement_id);
                    table.ForeignKey(
                        name: "FK_usr_user_program_achievements_profile",
                        column: x => x.profile_version_id,
                        principalTable: "usr_user_profile_versions",
                        principalColumn: "profile_version_id");
                    table.ForeignKey(
                        name: "FK_usr_user_program_achievements_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_program_instances",
                columns: table => new
                {
                    instance_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    program_id = table.Column<int>(type: "integer", nullable: false),
                    profile_version_id = table.Column<int>(type: "integer", nullable: false),
                    cycle_no = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    completed_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: true),
                    fitness_level_at_start = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    current_day_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    change_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__7DBD82E70F29266F", x => x.instance_id);
                    table.ForeignKey(
                        name: "FK_usr_user_program_instances_profile",
                        column: x => x.profile_version_id,
                        principalTable: "usr_user_profile_versions",
                        principalColumn: "profile_version_id");
                    table.ForeignKey(
                        name: "FK_usr_user_program_instances_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_usr_user_program_instances_wrk_program_templates_program_id",
                        column: x => x.program_id,
                        principalTable: "wrk_program_templates",
                        principalColumn: "program_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_workout_progress",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    profile_version_id = table.Column<int>(type: "integer", nullable: false),
                    workout_id = table.Column<int>(type: "integer", nullable: false),
                    current_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_step_no = table.Column<int>(type: "integer", nullable: false),
                    is_mastered = table.Column<bool>(type: "boolean", nullable: false),
                    mastered_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usr_user_workout_progress", x => new { x.user_id, x.profile_version_id, x.workout_id });
                    table.ForeignKey(
                        name: "FK_usr_user_workout_progress_profile",
                        column: x => x.profile_version_id,
                        principalTable: "usr_user_profile_versions",
                        principalColumn: "profile_version_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_usr_user_workout_progress_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "usr_user_general_achievements",
                columns: table => new
                {
                    achievement_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    badge_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    unlocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__3C492E83641FAE87", x => x.achievement_id);
                    table.ForeignKey(
                        name: "FK_UserBadges",
                        column: x => x.user_id,
                        principalTable: "usr_user_profiles",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "ntr_template_meal_items",
                columns: table => new
                {
                    template_item_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_meal_id = table.Column<int>(type: "integer", nullable: false),
                    food_id = table.Column<int>(type: "integer", nullable: false),
                    default_qty = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 1m),
                    is_optional_addon = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_temp__B820FECB489260B9", x => x.template_item_id);
                    table.ForeignKey(
                        name: "FK_ntr_template_meal_items_food",
                        column: x => x.food_id,
                        principalTable: "ntr_food_items",
                        principalColumn: "food_id");
                    table.ForeignKey(
                        name: "FK_ntr_template_meal_items_meal",
                        column: x => x.template_meal_id,
                        principalTable: "ntr_template_day_meals",
                        principalColumn: "template_meal_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ntr_daily_meal_item_logs",
                columns: table => new
                {
                    item_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    daily_log_id = table.Column<int>(type: "integer", nullable: false),
                    meal_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    food_id = table.Column<int>(type: "integer", nullable: false),
                    qty = table.Column<decimal>(type: "numeric(10,2)", nullable: false, defaultValue: 1m),
                    is_addon = table.Column<bool>(type: "boolean", nullable: false),
                    calories = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    protein_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    carbs_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    fats_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_dail__544A3BE4F65AAF65", x => x.item_log_id);
                    table.ForeignKey(
                        name: "FK_ntr_item_logs_daily",
                        column: x => x.daily_log_id,
                        principalTable: "ntr_daily_logs",
                        principalColumn: "daily_log_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ntr_item_logs_food",
                        column: x => x.food_id,
                        principalTable: "ntr_food_items",
                        principalColumn: "food_id");
                });

            migrationBuilder.CreateTable(
                name: "ntr_daily_meal_logs",
                columns: table => new
                {
                    meal_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    daily_log_id = table.Column<int>(type: "integer", nullable: false),
                    meal_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    calories = table.Column<int>(type: "integer", nullable: false),
                    protein_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    carbs_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    fats_g = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__ntr_dail__2AAEC3DFD622225B", x => x.meal_log_id);
                    table.ForeignKey(
                        name: "FK_ntr_daily_meal_logs_daily",
                        column: x => x.daily_log_id,
                        principalTable: "ntr_daily_logs",
                        principalColumn: "daily_log_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_progress_log",
                columns: table => new
                {
                    progress_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    instance_id = table.Column<int>(type: "integer", nullable: false),
                    month_no = table.Column<int>(type: "integer", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    fitness_level_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    calories_burned = table.Column<int>(type: "integer", nullable: true),
                    calories_intake = table.Column<int>(type: "integer", nullable: true),
                    water_ml = table.Column<int>(type: "integer", nullable: true),
                    meal_plan_completed = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__daily_pr__49B3D8C10C471706", x => x.progress_id);
                    table.ForeignKey(
                        name: "FK_daily_progress_log_instance",
                        column: x => x.instance_id,
                        principalTable: "usr_user_program_instances",
                        principalColumn: "instance_id");
                    table.ForeignKey(
                        name: "FK_daily_progress_log_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_session_instances",
                columns: table => new
                {
                    session_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instance_id = table.Column<int>(type: "integer", nullable: false),
                    month_no = table.Column<int>(type: "integer", nullable: false),
                    week_no = table.Column<int>(type: "integer", nullable: false),
                    day_no = table.Column<int>(type: "integer", nullable: false),
                    day_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "PLANNED"),
                    created_at = table.Column<DateTime>(type: "timestamp(0) with time zone", precision: 0, nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__69B13FDC28857219", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_usr_user_session_instances_instance",
                        column: x => x.instance_id,
                        principalTable: "usr_user_program_instances",
                        principalColumn: "instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_workout_sessions",
                columns: table => new
                {
                    session_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    program_instance_id = table.Column<int>(type: "integer", nullable: false),
                    workout_day = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__69B13FDC2EAF043E", x => x.session_id);
                    table.ForeignKey(
                        name: "FK_usr_user_workout_sessions_usr_user_program_instances_progra~",
                        column: x => x.program_instance_id,
                        principalTable: "usr_user_program_instances",
                        principalColumn: "instance_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_usr_user_workout_sessions_usr_users_user_id",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usr_user_session_workouts",
                columns: table => new
                {
                    session_workout_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<int>(type: "integer", nullable: false),
                    workout_id = table.Column<int>(type: "integer", nullable: false),
                    sets = table.Column<int>(type: "integer", nullable: false),
                    reps = table.Column<int>(type: "integer", nullable: false),
                    load_kg = table.Column<decimal>(type: "numeric(6,2)", nullable: true),
                    order_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__usr_user__538FD9C1874F2112", x => x.session_workout_id);
                    table.ForeignKey(
                        name: "FK_usr_user_session_workouts_usr_user_workout_sessions_session~",
                        column: x => x.session_id,
                        principalTable: "usr_user_workout_sessions",
                        principalColumn: "session_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_usr_user_session_workouts_wrk_workouts_workout_id",
                        column: x => x.workout_id,
                        principalTable: "wrk_workouts",
                        principalColumn: "workout_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_act_summary_user_date",
                table: "act_activity_summary",
                columns: new[] { "user_id", "log_date" });

            migrationBuilder.CreateIndex(
                name: "UX_act_activity_unique",
                table: "act_activity_summary",
                columns: new[] { "user_id", "log_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_progress_log_user_date",
                table: "daily_progress_log",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_daily_progress_log_unique",
                table: "daily_progress_log",
                columns: new[] { "instance_id", "month_no", "week_no", "day_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ntr_daily_logs_cycle_id",
                table: "ntr_daily_logs",
                column: "cycle_id");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_daily_logs_user_date",
                table: "ntr_daily_logs",
                columns: new[] { "user_id", "plan_date" });

            migrationBuilder.CreateIndex(
                name: "UX_ntr_daily_logs_unique",
                table: "ntr_daily_logs",
                columns: new[] { "user_id", "plan_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ntr_daily_item_logs_daily",
                table: "ntr_daily_meal_item_logs",
                columns: new[] { "daily_log_id", "meal_type" });

            migrationBuilder.CreateIndex(
                name: "IX_ntr_daily_meal_item_logs_food_id",
                table: "ntr_daily_meal_item_logs",
                column: "food_id");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_daily_meal_logs_daily",
                table: "ntr_daily_meal_logs",
                column: "daily_log_id");

            migrationBuilder.CreateIndex(
                name: "UX_ntr_daily_meal_logs_unique",
                table: "ntr_daily_meal_logs",
                columns: new[] { "daily_log_id", "meal_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ntr_food_allergies_allergy_id",
                table: "ntr_food_allergies",
                column: "allergy_id");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_food_dietary_type",
                table: "ntr_food_items",
                column: "dietary_type");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_food_meal_type",
                table: "ntr_food_items",
                column: "meal_type");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_food_search",
                table: "ntr_food_items",
                columns: new[] { "food_name", "size_type" });

            migrationBuilder.CreateIndex(
                name: "IX_ntr_calendar_cycle_date",
                table: "ntr_meal_plan_calendar",
                columns: new[] { "cycle_id", "plan_date" });

            migrationBuilder.CreateIndex(
                name: "IX_ntr_calendar_template",
                table: "ntr_meal_plan_calendar",
                columns: new[] { "template_id", "variation_code" });

            migrationBuilder.CreateIndex(
                name: "UX_ntr_calendar_unique",
                table: "ntr_meal_plan_calendar",
                columns: new[] { "cycle_id", "plan_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ntr_meal_templates_name",
                table: "ntr_meal_templates",
                column: "template_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ntr_template_day_meals",
                table: "ntr_template_day_meals",
                columns: new[] { "template_day_id", "meal_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ntr_template_days",
                table: "ntr_template_days",
                columns: new[] { "template_id", "variation_code", "day_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ntr_template_meal_items_food_id",
                table: "ntr_template_meal_items",
                column: "food_id");

            migrationBuilder.CreateIndex(
                name: "UX_ntr_template_item_unique",
                table: "ntr_template_meal_items",
                columns: new[] { "template_meal_id", "food_id", "is_optional_addon" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ntr_user_allergies_allergy_id",
                table: "ntr_user_allergies",
                column: "allergy_id");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_user_cycle_targets_user_id",
                table: "ntr_user_cycle_targets",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ntr_water_user_date",
                table: "ntr_water_logs",
                columns: new[] { "user_id", "log_date" });

            migrationBuilder.CreateIndex(
                name: "UX_ntr_water_unique",
                table: "ntr_water_logs",
                columns: new[] { "user_id", "log_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_device_tokens_user_id",
                table: "usr_device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "UX_usr_device_tokens_token",
                table: "usr_device_tokens",
                column: "fcm_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_notification_history_user_id",
                table: "usr_notification_history",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_general_achievements_user_id",
                table: "usr_user_general_achievements",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_metrics_user_date",
                table: "usr_user_metrics",
                columns: new[] { "user_id", "recorded_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_onboarding_details_user_id",
                table: "usr_user_onboarding_details",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_profile_versions_user_created",
                table: "usr_user_profile_versions",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_profile_versions_user_current",
                table: "usr_user_profile_versions",
                columns: new[] { "user_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "UX_usr_user_profile_versions_one_current",
                table: "usr_user_profile_versions",
                column: "user_id",
                unique: true,
                filter: "is_current = true");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_profiles_name_col",
                table: "usr_user_profiles",
                columns: new[] { "username", "name" });

            migrationBuilder.CreateIndex(
                name: "UQ__usr_user__B9BE370E6D7CFE4D",
                table: "usr_user_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_program_achievements_profile_version_id",
                table: "usr_user_program_achievements",
                column: "profile_version_id");

            migrationBuilder.CreateIndex(
                name: "UX_usr_user_program_achievements_unique",
                table: "usr_user_program_achievements",
                columns: new[] { "user_id", "program_id", "profile_version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_program_instances_profile_version_id",
                table: "usr_user_program_instances",
                column: "profile_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_program_instances_program_id",
                table: "usr_user_program_instances",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_program_instances_user_status",
                table: "usr_user_program_instances",
                columns: new[] { "user_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "UX_usr_user_program_instances_unique",
                table: "usr_user_program_instances",
                columns: new[] { "user_id", "program_id", "profile_version_id", "cycle_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_session_instances_status",
                table: "usr_user_session_instances",
                columns: new[] { "instance_id", "status" });

            migrationBuilder.CreateIndex(
                name: "UX_usr_user_session_instances_unique",
                table: "usr_user_session_instances",
                columns: new[] { "instance_id", "month_no", "week_no", "day_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_session_workouts_session_id",
                table: "usr_user_session_workouts",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_session_workouts_workout_id",
                table: "usr_user_session_workouts",
                column: "workout_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_workout_progress_profile_version_id",
                table: "usr_user_workout_progress",
                column: "profile_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_workout_sessions_program_instance_id",
                table: "usr_user_workout_sessions",
                column: "program_instance_id");

            migrationBuilder.CreateIndex(
                name: "IX_usr_user_workout_sessions_user_id",
                table: "usr_user_workout_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "UQ_usr_users_firebase_uid",
                table: "usr_users",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_usr_users_username",
                table: "usr_users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_usr_users_email",
                table: "usr_users",
                column: "email",
                unique: true,
                filter: "\"email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_usr_users_firebase_uid",
                table: "usr_users",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_usr_users_username",
                table: "usr_users",
                column: "username",
                unique: true,
                filter: "\"username\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_wkt_workout_calendars_unique",
                table: "wkt_workout_calendars",
                columns: new[] { "user_id", "plan_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wrk_program_template_days_lookup",
                table: "wrk_program_template_days",
                columns: new[] { "program_id", "month_no", "week_no", "day_no" });

            migrationBuilder.CreateIndex(
                name: "UX_wrk_program_template_days_unique",
                table: "wrk_program_template_days",
                columns: new[] { "program_id", "month_no", "week_no", "day_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wrk_daytype_w_lookup",
                table: "wrk_program_template_daytype_workouts",
                columns: new[] { "program_id", "day_type", "workout_order" });

            migrationBuilder.CreateIndex(
                name: "IX_wrk_program_template_daytype_workouts_workout_id",
                table: "wrk_program_template_daytype_workouts",
                column: "workout_id");

            migrationBuilder.CreateIndex(
                name: "UX_wrk_daytype_w_unique",
                table: "wrk_program_template_daytype_workouts",
                columns: new[] { "program_id", "day_type", "workout_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wrk_program_templates_filter",
                table: "wrk_program_templates",
                columns: new[] { "is_active", "program_category", "fitness_level", "session_structure", "environment", "equipment" });

            migrationBuilder.CreateIndex(
                name: "UX_wrk_program_templates_name",
                table: "wrk_program_templates",
                column: "program_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wrk_workout_load_steps_lookup",
                table: "wrk_workout_load_steps",
                columns: new[] { "workout_id", "level_name", "step_no" });

            migrationBuilder.CreateIndex(
                name: "UX_wrk_workout_load_steps_unique",
                table: "wrk_workout_load_steps",
                columns: new[] { "workout_id", "level_name", "step_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wrk_workouts_filter",
                table: "wrk_workouts",
                columns: new[] { "is_active", "muscle_group", "equipment", "environment", "category", "difficulty_level" });

            migrationBuilder.CreateIndex(
                name: "UX_wrk_workouts_name",
                table: "wrk_workouts",
                column: "workout_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "act_activity_summary");

            migrationBuilder.DropTable(
                name: "daily_progress_log");

            migrationBuilder.DropTable(
                name: "ntr_daily_meal_item_logs");

            migrationBuilder.DropTable(
                name: "ntr_daily_meal_logs");

            migrationBuilder.DropTable(
                name: "ntr_food_allergies");

            migrationBuilder.DropTable(
                name: "ntr_meal_plan_calendar");

            migrationBuilder.DropTable(
                name: "ntr_template_meal_items");

            migrationBuilder.DropTable(
                name: "ntr_user_allergies");

            migrationBuilder.DropTable(
                name: "ntr_user_nutrition_profile");

            migrationBuilder.DropTable(
                name: "ntr_water_logs");

            migrationBuilder.DropTable(
                name: "usr_device_tokens");

            migrationBuilder.DropTable(
                name: "usr_notification_history");

            migrationBuilder.DropTable(
                name: "usr_user_general_achievements");

            migrationBuilder.DropTable(
                name: "usr_user_metrics");

            migrationBuilder.DropTable(
                name: "usr_user_notification_settings");

            migrationBuilder.DropTable(
                name: "usr_user_onboarding_details");

            migrationBuilder.DropTable(
                name: "usr_user_program_achievements");

            migrationBuilder.DropTable(
                name: "usr_user_session_instances");

            migrationBuilder.DropTable(
                name: "usr_user_session_workouts");

            migrationBuilder.DropTable(
                name: "usr_user_workout_progress");

            migrationBuilder.DropTable(
                name: "wkt_workout_calendars");

            migrationBuilder.DropTable(
                name: "wrk_program_template_days");

            migrationBuilder.DropTable(
                name: "wrk_program_template_daytype_workouts");

            migrationBuilder.DropTable(
                name: "wrk_workout_load_steps");

            migrationBuilder.DropTable(
                name: "ntr_daily_logs");

            migrationBuilder.DropTable(
                name: "ntr_food_items");

            migrationBuilder.DropTable(
                name: "ntr_template_day_meals");

            migrationBuilder.DropTable(
                name: "ntr_allergies");

            migrationBuilder.DropTable(
                name: "usr_user_profiles");

            migrationBuilder.DropTable(
                name: "usr_user_workout_sessions");

            migrationBuilder.DropTable(
                name: "wrk_workouts");

            migrationBuilder.DropTable(
                name: "ntr_user_cycle_targets");

            migrationBuilder.DropTable(
                name: "ntr_template_days");

            migrationBuilder.DropTable(
                name: "usr_user_program_instances");

            migrationBuilder.DropTable(
                name: "ntr_meal_templates");

            migrationBuilder.DropTable(
                name: "usr_user_profile_versions");

            migrationBuilder.DropTable(
                name: "wrk_program_templates");

            migrationBuilder.DropTable(
                name: "usr_users");
        }
    }
}
