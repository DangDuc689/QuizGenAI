using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuizGenAI.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeModeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TargetBloomLevel",
                table: "QuizSets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPracticeMode",
                table: "ExamSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TargetBloomLevel",
                table: "QuizSets");

            migrationBuilder.DropColumn(
                name: "IsPracticeMode",
                table: "ExamSessions");
        }
    }
}
