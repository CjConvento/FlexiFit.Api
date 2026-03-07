using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Models;

public partial class FlexiFitDbContext : DbContext
{
    public FlexiFitDbContext()
    {
    }

    public FlexiFitDbContext(DbContextOptions<FlexiFitDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<UsrUserNotificationSetting> UsrUserNotificationSettings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=192.168.1.246,1433;Database=FLEXIFIT;User Id=cy;Password=;TrustServerCertificate=True");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
            entity.Property(e => e.MealReminderEnabled).HasColumnName("meal_reminder_enabled");
            entity.Property(e => e.MealReminderTime).HasColumnName("meal_reminder_time");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.WaterEndTime).HasColumnName("water_end_time");
            entity.Property(e => e.WaterIntervalMinutes).HasColumnName("water_interval_minutes");
            entity.Property(e => e.WaterReminderEnabled).HasColumnName("water_reminder_enabled");
            entity.Property(e => e.WaterStartTime).HasColumnName("water_start_time");
            entity.Property(e => e.WorkoutReminderEnabled).HasColumnName("workout_reminder_enabled");
            entity.Property(e => e.WorkoutReminderTime).HasColumnName("workout_reminder_time");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
