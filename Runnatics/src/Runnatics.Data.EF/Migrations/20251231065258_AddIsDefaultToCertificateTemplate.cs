using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Runnatics.Data.EF.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDefaultToCertificateTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundImageData",
                table: "CertificateTemplates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "CertificateTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "FieldType",
                table: "CertificateFields",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateTemplates_EventId_IsDefault",
                table: "CertificateTemplates",
                columns: new[] { "EventId", "IsDefault" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CertificateTemplates_EventId_IsDefault",
                table: "CertificateTemplates");

            migrationBuilder.DropColumn(
                name: "BackgroundImageData",
                table: "CertificateTemplates");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "CertificateTemplates");

            migrationBuilder.AlterColumn<string>(
                name: "FieldType",
                table: "CertificateFields",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
