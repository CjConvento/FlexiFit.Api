using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlexiFit.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyProgressLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_progress_log",
                columns: table => new
                {
                    progress_id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<int>(type: "int", nullable: false),
                    instance_id = table.Column<int>(type: "int", nullable: false),
                    month_no = table.Column<int>(type: "int", nullable: false),
                    week_no = table.Column<int>(type: "int", nullable: false),
                    day_no = table.Column<int>(type: "int", nullable: false),
                    fitness_level_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    calories_burned = table.Column<int>(type: "int", nullable: true),
                    calories_intake = table.Column<int>(type: "int", nullable: true),
                    water_ml = table.Column<int>(type: "int", nullable: true),
                    meal_plan_completed = table.Column<bool>(type: "bit", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    updated_at = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "sysutcdatetime()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_progress_log", x => x.progress_id);
                    table.ForeignKey(
                        name: "FK_daily_progress_log_instance",
                        column: x => x.instance_id,
                        principalTable: "usr_user_program_instances",
                        principalColumn: "instance_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_daily_progress_log_user",
                        column: x => x.user_id,
                        principalTable: "usr_users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_progress_log_user_date",
                table: "daily_progress_log",
                columns: new[] { "user_id", "created_at" })
                .Annotation("SqlServer:Clustered", false);

            migrationBuilder.CreateIndex(
                name: "UX_daily_progress_log_unique",
                table: "daily_progress_log",
                columns: new[] { "instance_id", "month_no", "week_no", "day_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_progress_log");
        }
    }
}