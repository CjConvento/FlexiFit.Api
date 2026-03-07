using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Entities;

public partial class FlexifitDbContext : DbContext
{
    public FlexifitDbContext()
    {
    }

    public FlexifitDbContext(DbContextOptions<FlexifitDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ActActivitySummary> ActActivitySummaries { get; set; }

    public virtual DbSet<DailyProgressLog> DailyProgressLogs { get; set; }

    public virtual DbSet<NtrDailyLog> NtrDailyLogs { get; set; }

    public virtual DbSet<NtrDailyMealItemLog> NtrDailyMealItemLogs { get; set; }

    public virtual DbSet<NtrDailyMealLog> NtrDailyMealLogs { get; set; }

    public virtual DbSet<NtrFoodItem> NtrFoodItems { get; set; }

    public virtual DbSet<NtrMealPlanCalendar> NtrMealPlanCalendars { get; set; }

    public virtual DbSet<NtrMealTemplate> NtrMealTemplates { get; set; }

    public virtual DbSet<NtrTemplateDay> NtrTemplateDays { get; set; }

    public virtual DbSet<NtrTemplateDayMeal> NtrTemplateDayMeals { get; set; }

    public virtual DbSet<NtrTemplateMealItem> NtrTemplateMealItems { get; set; }

    public virtual DbSet<NtrUserCycleTarget> NtrUserCycleTargets { get; set; }

    public virtual DbSet<NtrUserNutritionProfile> NtrUserNutritionProfiles { get; set; }

    public virtual DbSet<NtrWaterLog> NtrWaterLogs { get; set; }

    public virtual DbSet<UsrDeviceToken> UsrDeviceTokens { get; set; }

    public virtual DbSet<UsrUser> UsrUsers { get; set; }

    public virtual DbSet<UsrUserMetric> UsrUserMetrics { get; set; }

    public virtual DbSet<UsrUserNotificationSetting> UsrUserNotificationSettings { get; set; }

    public virtual DbSet<UsrUserProfile> UsrUserProfiles { get; set; }

    public virtual DbSet<UsrUserProfileVersion> UsrUserProfileVersions { get; set; }

    public virtual DbSet<UsrUserProgramAchievement> UsrUserProgramAchievements { get; set; }

    public virtual DbSet<UsrUserProgramInstance> UsrUserProgramInstances { get; set; }

    public virtual DbSet<UsrUserSessionInstance> UsrUserSessionInstances { get; set; }

    public virtual DbSet<UsrUserSessionWorkout> UsrUserSessionWorkouts { get; set; }

    public virtual DbSet<UsrUserWorkoutProgress> UsrUserWorkoutProgresses { get; set; }

    public virtual DbSet<VwNtrUserDailySummary> VwNtrUserDailySummaries { get; set; }

    public virtual DbSet<WrkProgramTemplate> WrkProgramTemplates { get; set; }

    public virtual DbSet<WrkProgramTemplateDay> WrkProgramTemplateDays { get; set; }

    public virtual DbSet<WrkProgramTemplateDaytypeWorkout> WrkProgramTemplateDaytypeWorkouts { get; set; }

    public virtual DbSet<WrkWorkout> WrkWorkouts { get; set; }

    public virtual DbSet<WrkWorkoutLoadStep> WrkWorkoutLoadSteps { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=DESKTOP-VKRMBR2\\SQLEXPRESS01;Database=FLEXIFIT;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActActivitySummary>(entity =>
        {
            entity.HasKey(e => e.SummaryId).HasName("PK__act_acti__85F93E83EE5828C3");

            entity.ToTable("act_activity_summary");

            entity.HasIndex(e => new { e.UserId, e.LogDate }, "IX_act_summary_user_date");

            entity.HasIndex(e => new { e.UserId, e.LogDate }, "UX_act_activity_unique").IsUnique();

            entity.Property(e => e.SummaryId).HasColumnName("summary_id");
            entity.Property(e => e.CaloriesBurned).HasColumnName("calories_burned");
            entity.Property(e => e.LogDate)
                .HasDefaultValueSql("(CONVERT([date],getdate()))")
                .HasColumnName("log_date");
            entity.Property(e => e.TotalMinutes).HasColumnName("total_minutes");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ActActivitySummaries)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_act_activity_user");
        });

        modelBuilder.Entity<DailyProgressLog>(entity =>
        {
            entity.HasKey(e => e.ProgressId).HasName("PK__daily_pr__49B3D8C10C471706");

            entity.ToTable("daily_progress_log");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_daily_progress_log_user_date").IsDescending(false, true);

            entity.HasIndex(e => new { e.InstanceId, e.MonthNo, e.WeekNo, e.DayNo }, "UX_daily_progress_log_unique").IsUnique();

            entity.Property(e => e.ProgressId).HasColumnName("progress_id");
            entity.Property(e => e.CaloriesBurned).HasColumnName("calories_burned");
            entity.Property(e => e.CaloriesIntake).HasColumnName("calories_intake");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DayNo).HasColumnName("day_no");
            entity.Property(e => e.FitnessLevelSnapshot)
                .HasMaxLength(20)
                .HasColumnName("fitness_level_snapshot");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.MealPlanCompleted).HasColumnName("meal_plan_completed");
            entity.Property(e => e.MonthNo).HasColumnName("month_no");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WaterMl).HasColumnName("water_ml");
            entity.Property(e => e.WeekNo).HasColumnName("week_no");

            entity.HasOne(d => d.Instance).WithMany(p => p.DailyProgressLogs)
                .HasForeignKey(d => d.InstanceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_daily_progress_log_instance");

            entity.HasOne(d => d.User).WithMany(p => p.DailyProgressLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_daily_progress_log_user");
        });

        modelBuilder.Entity<NtrDailyLog>(entity =>
        {
            entity.HasKey(e => e.DailyLogId).HasName("PK__ntr_dail__419B09B5C3B21459");

            entity.ToTable("ntr_daily_logs");

            entity.HasIndex(e => new { e.UserId, e.PlanDate }, "IX_ntr_daily_logs_user_date");

            entity.HasIndex(e => new { e.UserId, e.PlanDate }, "UX_ntr_daily_logs_unique").IsUnique();

            entity.Property(e => e.DailyLogId).HasColumnName("daily_log_id");
            entity.Property(e => e.CaloriesBurned).HasColumnName("calories_burned");
            entity.Property(e => e.CaloriesConsumed).HasColumnName("calories_consumed");
            entity.Property(e => e.CycleId).HasColumnName("cycle_id");
            entity.Property(e => e.GoalMet).HasColumnName("goal_met");
            entity.Property(e => e.GoalType)
                .HasMaxLength(20)
                .HasColumnName("goal_type");
            entity.Property(e => e.MarkedDoneAt)
                .HasPrecision(0)
                .HasColumnName("marked_done_at");
            entity.Property(e => e.NetCalories)
                .HasComputedColumnSql("([calories_consumed]-[calories_burned])", true)
                .HasColumnName("net_calories");
            entity.Property(e => e.PlanDate).HasColumnName("plan_date");
            entity.Property(e => e.TargetNetCalories).HasColumnName("target_net_calories");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Cycle).WithMany(p => p.NtrDailyLogs)
                .HasForeignKey(d => d.CycleId)
                .HasConstraintName("FK_ntr_daily_logs_cycle");

            entity.HasOne(d => d.User).WithMany(p => p.NtrDailyLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ntr_daily_logs_user");
        });

        modelBuilder.Entity<NtrDailyMealItemLog>(entity =>
        {
            entity.HasKey(e => e.ItemLogId).HasName("PK__ntr_dail__544A3BE4F65AAF65");

            entity.ToTable("ntr_daily_meal_item_logs");

            entity.HasIndex(e => new { e.DailyLogId, e.MealType }, "IX_ntr_daily_item_logs_daily");

            entity.Property(e => e.ItemLogId).HasColumnName("item_log_id");
            entity.Property(e => e.Calories)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("calories");
            entity.Property(e => e.CarbsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("carbs_g");
            entity.Property(e => e.DailyLogId).HasColumnName("daily_log_id");
            entity.Property(e => e.FatsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("fats_g");
            entity.Property(e => e.FoodId).HasColumnName("food_id");
            entity.Property(e => e.IsAddon).HasColumnName("is_addon");
            entity.Property(e => e.MealType)
                .HasMaxLength(10)
                .HasColumnName("meal_type");
            entity.Property(e => e.ProteinG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("protein_g");
            entity.Property(e => e.Qty)
                .HasDefaultValue(1m)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("qty");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(1)
                .HasColumnName("sort_order");

            entity.HasOne(d => d.DailyLog).WithMany(p => p.NtrDailyMealItemLogs)
                .HasForeignKey(d => d.DailyLogId)
                .HasConstraintName("FK_ntr_item_logs_daily");

            entity.HasOne(d => d.Food).WithMany(p => p.NtrDailyMealItemLogs)
                .HasForeignKey(d => d.FoodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ntr_item_logs_food");
        });

        modelBuilder.Entity<NtrDailyMealLog>(entity =>
        {
            entity.HasKey(e => e.MealLogId).HasName("PK__ntr_dail__2AAEC3DFD622225B");

            entity.ToTable("ntr_daily_meal_logs");

            entity.HasIndex(e => e.DailyLogId, "IX_ntr_daily_meal_logs_daily");

            entity.HasIndex(e => new { e.DailyLogId, e.MealType }, "UX_ntr_daily_meal_logs_unique").IsUnique();

            entity.Property(e => e.MealLogId).HasColumnName("meal_log_id");
            entity.Property(e => e.Calories).HasColumnName("calories");
            entity.Property(e => e.CarbsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("carbs_g");
            entity.Property(e => e.DailyLogId).HasColumnName("daily_log_id");
            entity.Property(e => e.FatsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("fats_g");
            entity.Property(e => e.MealType)
                .HasMaxLength(10)
                .HasColumnName("meal_type");
            entity.Property(e => e.ProteinG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("protein_g");

            entity.HasOne(d => d.DailyLog).WithMany(p => p.NtrDailyMealLogs)
                .HasForeignKey(d => d.DailyLogId)
                .HasConstraintName("FK_ntr_daily_meal_logs_daily");
        });

        modelBuilder.Entity<NtrFoodItem>(entity =>
        {
            entity.HasKey(e => e.FoodId).HasName("PK__ntr_food__2F4C4DD8703F737A");

            entity.ToTable("ntr_food_items");

            entity.HasIndex(e => e.DietaryType, "IX_ntr_food_dietary_type");

            entity.HasIndex(e => e.MealType, "IX_ntr_food_meal_type");

            entity.HasIndex(e => new { e.FoodName, e.SizeType }, "IX_ntr_food_search");

            entity.Property(e => e.FoodId).HasColumnName("food_id");
            entity.Property(e => e.Calories)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("calories");
            entity.Property(e => e.CarbsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("carbs_g");
            entity.Property(e => e.Category)
                .HasMaxLength(80)
                .HasDefaultValue("Food")
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DietaryType)
                .HasMaxLength(40)
                .HasColumnName("dietary_type");
            entity.Property(e => e.FatsG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("fats_g");
            entity.Property(e => e.FoodName)
                .HasMaxLength(255)
                .HasColumnName("food_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MealType)
                .HasMaxLength(30)
                .HasColumnName("meal_type");
            entity.Property(e => e.ProteinG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("protein_g");
            entity.Property(e => e.ServingUnit)
                .HasMaxLength(30)
                .HasDefaultValue("Serving")
                .HasColumnName("serving_unit");
            entity.Property(e => e.ServingWeightG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("serving_weight_g");
            entity.Property(e => e.SizeType)
                .HasMaxLength(20)
                .HasDefaultValue("Regular")
                .HasColumnName("size_type");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<NtrMealPlanCalendar>(entity =>
        {
            entity.HasKey(e => e.CalendarId).HasName("PK__ntr_meal__584C1344FF6CEFBC");

            entity.ToTable("ntr_meal_plan_calendar");

            entity.HasIndex(e => new { e.CycleId, e.PlanDate }, "IX_ntr_calendar_cycle_date");

            entity.HasIndex(e => new { e.TemplateId, e.VariationCode }, "IX_ntr_calendar_template");

            entity.HasIndex(e => new { e.CycleId, e.PlanDate }, "UX_ntr_calendar_unique").IsUnique();

            entity.Property(e => e.CalendarId).HasColumnName("calendar_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CycleId).HasColumnName("cycle_id");
            entity.Property(e => e.DayNo).HasColumnName("day_no");
            entity.Property(e => e.IsWorkoutDay).HasColumnName("is_workout_day");
            entity.Property(e => e.PlanDate).HasColumnName("plan_date");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.TemplateId).HasColumnName("template_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.VariationCode)
                .HasMaxLength(10)
                .HasDefaultValue("A")
                .HasColumnName("variation_code");
            entity.Property(e => e.WeekNo).HasColumnName("week_no");

            entity.HasOne(d => d.Cycle).WithMany(p => p.NtrMealPlanCalendars)
                .HasForeignKey(d => d.CycleId)
                .HasConstraintName("FK_ntr_calendar_cycle");

            entity.HasOne(d => d.Template).WithMany(p => p.NtrMealPlanCalendars)
                .HasForeignKey(d => d.TemplateId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ntr_calendar_template");
        });

        modelBuilder.Entity<NtrMealTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId).HasName("PK__ntr_meal__BE44E0790A220F73");

            entity.ToTable("ntr_meal_templates");

            entity.HasIndex(e => e.TemplateName, "UX_ntr_meal_templates_name").IsUnique();

            entity.Property(e => e.TemplateId).HasColumnName("template_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DietaryType)
                .HasMaxLength(40)
                .HasColumnName("dietary_type");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.TemplateName)
                .HasMaxLength(120)
                .HasColumnName("template_name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<NtrTemplateDay>(entity =>
        {
            entity.HasKey(e => e.TemplateDayId).HasName("PK__ntr_temp__F828DE9A4CE042C3");

            entity.ToTable("ntr_template_days");

            entity.HasIndex(e => new { e.TemplateId, e.VariationCode, e.DayNo }, "UX_ntr_template_days").IsUnique();

            entity.Property(e => e.TemplateDayId).HasColumnName("template_day_id");
            entity.Property(e => e.DayNo).HasColumnName("day_no");
            entity.Property(e => e.Notes)
                .HasMaxLength(200)
                .HasColumnName("notes");
            entity.Property(e => e.TemplateId).HasColumnName("template_id");
            entity.Property(e => e.VariationCode)
                .HasMaxLength(10)
                .HasDefaultValue("A")
                .HasColumnName("variation_code");

            entity.HasOne(d => d.Template).WithMany(p => p.NtrTemplateDays)
                .HasForeignKey(d => d.TemplateId)
                .HasConstraintName("FK_ntr_template_days_template");
        });

        modelBuilder.Entity<NtrTemplateDayMeal>(entity =>
        {
            entity.HasKey(e => e.TemplateMealId).HasName("PK__ntr_temp__B793799AAFE48243");

            entity.ToTable("ntr_template_day_meals");

            entity.HasIndex(e => new { e.TemplateDayId, e.MealType }, "UX_ntr_template_day_meals").IsUnique();

            entity.Property(e => e.TemplateMealId).HasColumnName("template_meal_id");
            entity.Property(e => e.MealType)
                .HasMaxLength(10)
                .HasColumnName("meal_type");
            entity.Property(e => e.TargetSharePct)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("target_share_pct");
            entity.Property(e => e.TemplateDayId).HasColumnName("template_day_id");

            entity.HasOne(d => d.TemplateDay).WithMany(p => p.NtrTemplateDayMeals)
                .HasForeignKey(d => d.TemplateDayId)
                .HasConstraintName("FK_ntr_template_day_meals_day");
        });

        modelBuilder.Entity<NtrTemplateMealItem>(entity =>
        {
            entity.HasKey(e => e.TemplateItemId).HasName("PK__ntr_temp__B820FECB489260B9");

            entity.ToTable("ntr_template_meal_items");

            entity.HasIndex(e => new { e.TemplateMealId, e.FoodId, e.IsOptionalAddon }, "UX_ntr_template_item_unique").IsUnique();

            entity.Property(e => e.TemplateItemId).HasColumnName("template_item_id");
            entity.Property(e => e.DefaultQty)
                .HasDefaultValue(1m)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("default_qty");
            entity.Property(e => e.FoodId).HasColumnName("food_id");
            entity.Property(e => e.IsOptionalAddon).HasColumnName("is_optional_addon");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(1)
                .HasColumnName("sort_order");
            entity.Property(e => e.TemplateMealId).HasColumnName("template_meal_id");

            entity.HasOne(d => d.Food).WithMany(p => p.NtrTemplateMealItems)
                .HasForeignKey(d => d.FoodId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ntr_template_meal_items_food");

            entity.HasOne(d => d.TemplateMeal).WithMany(p => p.NtrTemplateMealItems)
                .HasForeignKey(d => d.TemplateMealId)
                .HasConstraintName("FK_ntr_template_meal_items_meal");
        });

        modelBuilder.Entity<NtrUserCycleTarget>(entity =>
        {
            entity.HasKey(e => e.CycleId).HasName("PK__ntr_user__5D9558815CC833BD");

            entity.ToTable("ntr_user_cycle_targets");

            entity.Property(e => e.CycleId).HasColumnName("cycle_id");
            entity.Property(e => e.CarbsTargetG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("carbs_target_g");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DailyTargetNetCalories).HasColumnName("daily_target_net_calories");
            entity.Property(e => e.FatsTargetG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("fats_target_g");
            entity.Property(e => e.GoalType)
                .HasMaxLength(20)
                .HasColumnName("goal_type");
            entity.Property(e => e.ProteinTargetG)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("protein_target_g");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WeeksInCycle)
                .HasDefaultValue(4)
                .HasColumnName("weeks_in_cycle");

            entity.HasOne(d => d.User).WithMany(p => p.NtrUserCycleTargets)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ntr_cycle_user");
        });

        modelBuilder.Entity<NtrUserNutritionProfile>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__ntr_user__B9BE370FB0DF2DB0");

            entity.ToTable("ntr_user_nutrition_profile");

            entity.Property(e => e.UserId)
                .ValueGeneratedNever()
                .HasColumnName("user_id");
            entity.Property(e => e.ActivityLevel)
                .HasMaxLength(20)
                .HasColumnName("activity_level");
            entity.Property(e => e.Age).HasColumnName("age");
            entity.Property(e => e.DietaryType)
                .HasMaxLength(40)
                .HasColumnName("dietary_type");
            entity.Property(e => e.HeightCm)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("height_cm");
            entity.Property(e => e.NutritionGoal)
                .HasMaxLength(20)
                .HasColumnName("nutrition_goal");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeightKg)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("weight_kg");

            entity.HasOne(d => d.User).WithOne(p => p.NtrUserNutritionProfile)
                .HasForeignKey<NtrUserNutritionProfile>(d => d.UserId)
                .HasConstraintName("FK_ntr_profile_user");
        });

        modelBuilder.Entity<NtrWaterLog>(entity =>
        {
            entity.HasKey(e => e.WaterLogId).HasName("PK__ntr_wate__0618B2D221BCBA83");

            entity.ToTable("ntr_water_logs");

            entity.HasIndex(e => new { e.UserId, e.LogDate }, "IX_ntr_water_user_date");

            entity.HasIndex(e => new { e.UserId, e.LogDate }, "UX_ntr_water_unique").IsUnique();

            entity.Property(e => e.WaterLogId).HasColumnName("water_log_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.LogDate)
                .HasDefaultValueSql("(CONVERT([date],getdate()))")
                .HasColumnName("log_date");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WaterMl).HasColumnName("water_ml");

            entity.HasOne(d => d.User).WithMany(p => p.NtrWaterLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_ntr_water_user_link");
        });

        modelBuilder.Entity<UsrDeviceToken>(entity =>
        {
            entity.HasKey(e => e.DeviceTokenId).HasName("PK__usr_devi__3ADABB7DC0BD9F4B");

            entity.ToTable("usr_device_tokens");

            entity.HasIndex(e => e.FcmToken, "UX_usr_device_tokens_token").IsUnique();

            entity.Property(e => e.DeviceTokenId).HasColumnName("device_token_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.FcmToken)
                .HasMaxLength(255)
                .HasColumnName("fcm_token");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Platform)
                .HasMaxLength(30)
                .HasDefaultValue("android")
                .HasColumnName("platform");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UsrDeviceTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_usr_device_tokens_user");
        });

        modelBuilder.Entity<UsrUser>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__usr_user__B9BE370FB3CBFE25");

            entity.ToTable("usr_users");

            entity.HasIndex(e => e.Email, "UX_usr_users_email")
                .IsUnique()
                .HasFilter("([email] IS NOT NULL)");

            entity.HasIndex(e => e.FirebaseUid, "UX_usr_users_firebase_uid").IsUnique();

            entity.HasIndex(e => e.Username, "UX_usr_users_username")
                .IsUnique()
                .HasFilter("([username] IS NOT NULL)");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Address)
                .HasMaxLength(255)
                .HasColumnName("address");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FirebaseUid)
                .HasMaxLength(128)
                .HasColumnName("firebase_uid");
            entity.Property(e => e.IsVerified).HasColumnName("is_verified");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Member")
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasDefaultValue("Active")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
        });

        modelBuilder.Entity<UsrUserMetric>(entity =>
        {
            entity.HasKey(e => e.MetricId).HasName("PK__usr_user__13D5DCA4F6173695");

            entity.ToTable("usr_user_metrics");

            entity.HasIndex(e => new { e.UserId, e.RecordedAt }, "IX_usr_user_metrics_user_date").IsDescending(false, true);

            entity.Property(e => e.MetricId).HasColumnName("metric_id");
            entity.Property(e => e.CalorieTarget).HasColumnName("calorie_target");
            entity.Property(e => e.CarbsTargetG).HasColumnName("carbs_target_g");
            entity.Property(e => e.CurrentHeightCm)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("current_height_cm");
            entity.Property(e => e.CurrentWeightKg)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("current_weight_kg");
            entity.Property(e => e.FatsTargetG).HasColumnName("fats_target_g");
            entity.Property(e => e.FitnessGoal)
                .HasMaxLength(50)
                .HasDefaultValue("Strength")
                .HasColumnName("fitness_goal");
            entity.Property(e => e.NutritionGoal)
                .HasMaxLength(50)
                .HasDefaultValue("Maintain")
                .HasColumnName("nutrition_goal");
            entity.Property(e => e.ProteinTargetG).HasColumnName("protein_target_g");
            entity.Property(e => e.RecordedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("recorded_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UsrUserMetrics)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_usr_user_metrics_user");
        });

        modelBuilder.Entity<UsrUserNotificationSetting>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__user_not__B9BE370F9E422610");

            entity.ToTable("usr_user_notification_settings");

            entity.Property(e => e.UserId)
                .ValueGeneratedNever()
                .HasColumnName("user_id");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("created_at");

            entity.Property(e => e.MealReminderEnabled)
                .HasColumnName("meal_reminder_enabled");

            entity.Property(e => e.MealReminderTime)
                .HasColumnName("meal_reminder_time");

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");

            entity.Property(e => e.WaterEndTime)
                .HasColumnName("water_end_time");

            entity.Property(e => e.WaterIntervalMinutes)
                .HasColumnName("water_interval_minutes");

            entity.Property(e => e.WaterReminderEnabled)
                .HasColumnName("water_reminder_enabled");

            entity.Property(e => e.WaterStartTime)
                .HasColumnName("water_start_time");

            entity.Property(e => e.WorkoutReminderEnabled)
                .HasColumnName("workout_reminder_enabled");

            entity.Property(e => e.WorkoutReminderTime)
                .HasColumnName("workout_reminder_time");

            entity.HasOne(d => d.User)
                .WithOne(p => p.UsrUserNotificationSetting)
                .HasForeignKey<UsrUserNotificationSetting>(d => d.UserId)
                .HasConstraintName("FK_user_notification_settings_user");
        });

        modelBuilder.Entity<UsrUserProfile>(entity =>
        {
            entity.HasKey(e => e.ProfileId).HasName("PK__usr_user__AEBB701FD9D16A8B");

            entity.ToTable("usr_user_profiles");

            entity.HasIndex(e => new { e.Username, e.Name }, "IX_usr_user_profiles_name_col");

            entity.HasIndex(e => e.UserId, "UQ__usr_user__B9BE370E6D7CFE4D").IsUnique();

            entity.Property(e => e.ProfileId).HasColumnName("profile_id");
            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(500)
                .HasColumnName("avatar_url");
            entity.Property(e => e.BirthDate).HasColumnName("birth_date");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.Gender)
                .HasMaxLength(10)
                .IsUnicode(false)
                .HasColumnName("gender");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username");

            entity.HasOne(d => d.User).WithOne(p => p.UsrUserProfile)
                .HasForeignKey<UsrUserProfile>(d => d.UserId)
                .HasConstraintName("FK_usr_user_profiles_user");
        });

        modelBuilder.Entity<UsrUserProfileVersion>(entity =>
        {
            entity.HasKey(e => e.ProfileVersionId).HasName("PK__usr_user__87F5126507C5745C");

            entity.ToTable("usr_user_profile_versions");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "IX_usr_user_profile_versions_user_created").IsDescending(false, true);

            entity.HasIndex(e => new { e.UserId, e.IsCurrent }, "IX_usr_user_profile_versions_user_current");

            entity.HasIndex(e => e.UserId, "UX_usr_user_profile_versions_one_current")
                .IsUnique()
                .HasFilter("([is_current]=(1))");

            entity.Property(e => e.ProfileVersionId).HasColumnName("profile_version_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.FitnessLevelSelected)
                .HasMaxLength(20)
                .HasColumnName("fitness_level_selected");
            entity.Property(e => e.GoalSelected)
                .HasMaxLength(30)
                .HasColumnName("goal_selected");
            entity.Property(e => e.IsCurrent).HasColumnName("is_current");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithOne(p => p.UsrUserProfileVersion)
                .HasForeignKey<UsrUserProfileVersion>(d => d.UserId)
                .HasConstraintName("FK_usr_user_profile_versions_user");
        });

        modelBuilder.Entity<UsrUserProgramAchievement>(entity =>
        {
            entity.HasKey(e => e.AchievementId).HasName("PK__usr_user__3C492E83B02C8E3C");

            entity.ToTable("usr_user_program_achievements");

            entity.HasIndex(e => new { e.UserId, e.ProgramId, e.ProfileVersionId }, "UX_usr_user_program_achievements_unique").IsUnique();

            entity.Property(e => e.AchievementId).HasColumnName("achievement_id");
            entity.Property(e => e.CompletedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("completed_at");
            entity.Property(e => e.CompletedCount)
                .HasDefaultValue(1)
                .HasColumnName("completed_count");
            entity.Property(e => e.ProfileVersionId).HasColumnName("profile_version_id");
            entity.Property(e => e.ProgramId).HasColumnName("program_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("COMPLETED")
                .HasColumnName("status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.ProfileVersion).WithMany(p => p.UsrUserProgramAchievements)
                .HasForeignKey(d => d.ProfileVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_usr_user_program_achievements_profile");

            entity.HasOne(d => d.User).WithMany(p => p.UsrUserProgramAchievements)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_usr_user_program_achievements_user");
        });

        modelBuilder.Entity<UsrUserProgramInstance>(entity =>
        {
            entity.HasKey(e => e.InstanceId).HasName("PK__usr_user__7DBD82E70F29266F");

            entity.ToTable("usr_user_program_instances");

            entity.HasIndex(e => new { e.UserId, e.Status, e.CreatedAt }, "IX_usr_user_program_instances_user_status").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.UserId, e.ProgramId, e.ProfileVersionId, e.CycleNo }, "UX_usr_user_program_instances_unique").IsUnique();

            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.CompletedAt)
                .HasPrecision(0)
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.CycleNo).HasColumnName("cycle_no");
            entity.Property(e => e.FitnessLevelAtStart)
                .HasMaxLength(20)
                .HasColumnName("fitness_level_at_start");
            entity.Property(e => e.ProfileVersionId).HasColumnName("profile_version_id");
            entity.Property(e => e.ProgramId).HasColumnName("program_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE")
                .HasColumnName("status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.ProfileVersion).WithMany(p => p.UsrUserProgramInstances)
                .HasForeignKey(d => d.ProfileVersionId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_usr_user_program_instances_profile");

            entity.HasOne(d => d.User).WithMany(p => p.UsrUserProgramInstances)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_usr_user_program_instances_user");
        });

        modelBuilder.Entity<UsrUserSessionInstance>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__usr_user__69B13FDC28857219");

            entity.ToTable("usr_user_session_instances");

            entity.HasIndex(e => new { e.InstanceId, e.Status }, "IX_usr_user_session_instances_status");

            entity.HasIndex(e => new { e.InstanceId, e.MonthNo, e.WeekNo, e.DayNo }, "UX_usr_user_session_instances_unique").IsUnique();

            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DayNo).HasColumnName("day_no");
            entity.Property(e => e.DayType)
                .HasMaxLength(30)
                .HasColumnName("day_type");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.MonthNo).HasColumnName("month_no");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PLANNED")
                .HasColumnName("status");
            entity.Property(e => e.WeekNo).HasColumnName("week_no");

            entity.HasOne(d => d.Instance).WithMany(p => p.UsrUserSessionInstances)
                .HasForeignKey(d => d.InstanceId)
                .HasConstraintName("FK_usr_user_session_instances_instance");
        });

        modelBuilder.Entity<UsrUserSessionWorkout>(entity =>
        {
            entity.HasKey(e => e.SessionWorkoutId).HasName("PK__usr_user__538FD9C1874F2112");

            entity.ToTable("usr_user_session_workouts");

            entity.HasIndex(e => new { e.SessionId, e.OrderNo }, "IX_usr_user_session_workouts_session");

            entity.Property(e => e.SessionWorkoutId).HasColumnName("session_workout_id");
            entity.Property(e => e.LoadKg)
                .HasColumnType("decimal(6, 2)")
                .HasColumnName("load_kg");
            entity.Property(e => e.OrderNo)
                .HasDefaultValue(1)
                .HasColumnName("order_no");
            entity.Property(e => e.Reps).HasColumnName("reps");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Sets).HasColumnName("sets");
            entity.Property(e => e.WorkoutId).HasColumnName("workout_id");

            entity.HasOne(d => d.Session).WithMany(p => p.UsrUserSessionWorkouts)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("FK_usr_user_session_workouts_session");
        });

        modelBuilder.Entity<UsrUserWorkoutProgress>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.ProfileVersionId, e.WorkoutId });

            entity.ToTable("usr_user_workout_progress");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ProfileVersionId).HasColumnName("profile_version_id");
            entity.Property(e => e.WorkoutId).HasColumnName("workout_id");
            entity.Property(e => e.CurrentLevel)
                .HasMaxLength(20)
                .HasColumnName("current_level");
            entity.Property(e => e.CurrentStepNo).HasColumnName("current_step_no");
            entity.Property(e => e.IsMastered).HasColumnName("is_mastered");
            entity.Property(e => e.MasteredAt)
                .HasPrecision(0)
                .HasColumnName("mastered_at");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ProfileVersion).WithMany(p => p.UsrUserWorkoutProgresses)
                .HasForeignKey(d => d.ProfileVersionId)
                .HasConstraintName("FK_usr_user_workout_progress_profile");

            entity.HasOne(d => d.User).WithMany(p => p.UsrUserWorkoutProgresses)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_usr_user_workout_progress_user");
        });

        modelBuilder.Entity<VwNtrUserDailySummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_ntr_user_daily_summary");

            entity.Property(e => e.ActivityBurnedKcal).HasColumnName("activity_burned_kcal");
            entity.Property(e => e.ActivityMinutes).HasColumnName("activity_minutes");
            entity.Property(e => e.CaloriesBurned).HasColumnName("calories_burned");
            entity.Property(e => e.CaloriesConsumed).HasColumnName("calories_consumed");
            entity.Property(e => e.GoalMet).HasColumnName("goal_met");
            entity.Property(e => e.GoalType)
                .HasMaxLength(20)
                .HasColumnName("goal_type");
            entity.Property(e => e.LogDate).HasColumnName("log_date");
            entity.Property(e => e.MarkedDoneAt)
                .HasPrecision(0)
                .HasColumnName("marked_done_at");
            entity.Property(e => e.NetCalories).HasColumnName("net_calories");
            entity.Property(e => e.TargetNetCalories).HasColumnName("target_net_calories");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WaterMl).HasColumnName("water_ml");
        });

        modelBuilder.Entity<WrkProgramTemplate>(entity =>
        {
            entity.HasKey(e => e.ProgramId).HasName("PK__wrk_prog__3A7890AC32B98A84");

            entity.ToTable("wrk_program_templates");

            entity.HasIndex(e => new { e.IsActive, e.ProgramCategory, e.FitnessLevel, e.SessionStructure, e.Environment, e.Equipment }, "IX_wrk_program_templates_filter");

            entity.HasIndex(e => e.ProgramName, "UX_wrk_program_templates_name").IsUnique();

            entity.Property(e => e.ProgramId).HasColumnName("program_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DaysPerWeek)
                .HasDefaultValue(7)
                .HasColumnName("days_per_week");
            entity.Property(e => e.Environment)
                .HasMaxLength(30)
                .HasColumnName("environment");
            entity.Property(e => e.Equipment)
                .HasMaxLength(50)
                .HasColumnName("equipment");
            entity.Property(e => e.FitnessLevel)
                .HasMaxLength(20)
                .HasColumnName("fitness_level");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MonthsPerCycle)
                .HasDefaultValue(1)
                .HasColumnName("months_per_cycle");
            entity.Property(e => e.ProgramCategory)
                .HasMaxLength(30)
                .HasColumnName("program_category");
            entity.Property(e => e.ProgramName)
                .HasMaxLength(120)
                .HasColumnName("program_name");
            entity.Property(e => e.SessionStructure)
                .HasMaxLength(20)
                .HasColumnName("session_structure");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeeksPerMonth)
                .HasDefaultValue(4)
                .HasColumnName("weeks_per_month");
        });

        modelBuilder.Entity<WrkProgramTemplateDay>(entity =>
        {
            entity.HasKey(e => e.TemplateDayId).HasName("PK__wrk_prog__F828DE9A6177FA3E");

            entity.ToTable("wrk_program_template_days");

            entity.HasIndex(e => new { e.ProgramId, e.MonthNo, e.WeekNo, e.DayNo }, "IX_wrk_program_template_days_lookup");

            entity.HasIndex(e => new { e.ProgramId, e.MonthNo, e.WeekNo, e.DayNo }, "UX_wrk_program_template_days_unique").IsUnique();

            entity.Property(e => e.TemplateDayId).HasColumnName("template_day_id");
            entity.Property(e => e.DayNo).HasColumnName("day_no");
            entity.Property(e => e.DayType)
                .HasMaxLength(30)
                .HasColumnName("day_type");
            entity.Property(e => e.MonthNo).HasColumnName("month_no");
            entity.Property(e => e.Notes)
                .HasMaxLength(300)
                .HasColumnName("notes");
            entity.Property(e => e.ProgramId).HasColumnName("program_id");
            entity.Property(e => e.WeekNo).HasColumnName("week_no");

            entity.HasOne(d => d.Program).WithMany(p => p.WrkProgramTemplateDays)
                .HasForeignKey(d => d.ProgramId)
                .HasConstraintName("FK_wrk_program_template_days_program");
        });

        modelBuilder.Entity<WrkProgramTemplateDaytypeWorkout>(entity =>
        {
            entity.HasKey(e => e.DaytypeWId).HasName("PK__wrk_prog__C5C1481DAEF5B917");

            entity.ToTable("wrk_program_template_daytype_workouts");

            entity.HasIndex(e => new { e.ProgramId, e.DayType, e.WorkoutOrder }, "IX_wrk_daytype_w_lookup");

            entity.HasIndex(e => new { e.ProgramId, e.DayType, e.WorkoutOrder }, "UX_wrk_daytype_w_unique").IsUnique();

            entity.Property(e => e.DaytypeWId).HasColumnName("daytype_w_id");
            entity.Property(e => e.DayType)
                .HasMaxLength(30)
                .HasColumnName("day_type");
            entity.Property(e => e.IsPrimaryLift).HasColumnName("is_primary_lift");
            entity.Property(e => e.MusclePriority)
                .HasMaxLength(50)
                .HasColumnName("muscle_priority");
            entity.Property(e => e.ProgramId).HasColumnName("program_id");
            entity.Property(e => e.RepsDefault).HasColumnName("reps_default");
            entity.Property(e => e.RestSeconds).HasColumnName("rest_seconds");
            entity.Property(e => e.SetsDefault).HasColumnName("sets_default");
            entity.Property(e => e.WorkoutId).HasColumnName("workout_id");
            entity.Property(e => e.WorkoutOrder)
                .HasDefaultValue(1)
                .HasColumnName("workout_order");

            entity.HasOne(d => d.Program).WithMany(p => p.WrkProgramTemplateDaytypeWorkouts)
                .HasForeignKey(d => d.ProgramId)
                .HasConstraintName("FK_wrk_daytype_w_program");

            entity.HasOne(d => d.Workout).WithMany(p => p.WrkProgramTemplateDaytypeWorkouts)
                .HasForeignKey(d => d.WorkoutId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_wrk_daytype_w_workout");
        });

        modelBuilder.Entity<WrkWorkout>(entity =>
        {
            entity.HasKey(e => e.WorkoutId).HasName("PK__wrk_work__02AB2F8E16CDC0FE");

            entity.ToTable("wrk_workouts");

            entity.HasIndex(e => new { e.IsActive, e.MuscleGroup, e.Equipment, e.Environment, e.Category, e.DifficultyLevel }, "IX_wrk_workouts_filter");

            entity.HasIndex(e => e.WorkoutName, "UX_wrk_workouts_name").IsUnique();

            entity.Property(e => e.WorkoutId).HasColumnName("workout_id");
            entity.Property(e => e.CaloriesBurned)
                .HasDefaultValue(0)
                .HasColumnName("calories_burned");
            entity.Property(e => e.Category)
                .HasMaxLength(50)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.DifficultyLevel)
                .HasMaxLength(20)
                .HasColumnName("difficulty_level");
            entity.Property(e => e.Environment)
                .HasMaxLength(30)
                .HasColumnName("environment");
            entity.Property(e => e.Equipment)
                .HasMaxLength(50)
                .HasColumnName("equipment");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsWeighted).HasColumnName("is_weighted");
            entity.Property(e => e.MuscleGroup)
                .HasMaxLength(50)
                .HasColumnName("muscle_group");
            entity.Property(e => e.Notes)
                .HasMaxLength(500)
                .HasColumnName("notes");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("updated_at");
            entity.Property(e => e.VideoUrl)
                .HasMaxLength(500)
                .HasColumnName("video_url");
            entity.Property(e => e.WorkoutName)
                .HasMaxLength(150)
                .HasColumnName("workout_name");
        });

        modelBuilder.Entity<WrkWorkoutLoadStep>(entity =>
        {
            entity.HasKey(e => e.LoadStepId).HasName("PK__wrk_work__405968146EE249E6");

            entity.ToTable("wrk_workout_load_steps");

            entity.HasIndex(e => new { e.WorkoutId, e.LevelName, e.StepNo }, "IX_wrk_workout_load_steps_lookup");

            entity.HasIndex(e => new { e.WorkoutId, e.LevelName, e.StepNo }, "UX_wrk_workout_load_steps_unique").IsUnique();

            entity.Property(e => e.LoadStepId).HasColumnName("load_step_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(0)
                .HasDefaultValueSql("(sysutcdatetime())")
                .HasColumnName("created_at");
            entity.Property(e => e.LevelName)
                .HasMaxLength(20)
                .HasColumnName("level_name");
            entity.Property(e => e.LoadKg)
                .HasColumnType("decimal(6, 2)")
                .HasColumnName("load_kg");
            entity.Property(e => e.StepNo).HasColumnName("step_no");
            entity.Property(e => e.WorkoutId).HasColumnName("workout_id");

            entity.HasOne(d => d.Workout).WithMany(p => p.WrkWorkoutLoadSteps)
                .HasForeignKey(d => d.WorkoutId)
                .HasConstraintName("FK_wrk_workout_load_steps_workout");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
