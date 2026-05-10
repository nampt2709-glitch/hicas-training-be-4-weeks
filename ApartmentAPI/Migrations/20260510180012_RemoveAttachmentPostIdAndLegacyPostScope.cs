using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApartmentAPI.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAttachmentPostIdAndLegacyPostScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Giá trị enum cũ AttachmentScope.Post = 2 không còn trong mã — chuẩn hoá về Feedback (1) trước khi xoá cột.
            migrationBuilder.Sql("UPDATE [Attachments] SET [Scope] = 1 WHERE [Scope] = 2;");
            migrationBuilder.DropColumn(
                name: "PostId",
                table: "Attachments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PostId",
                table: "Attachments",
                type: "uniqueidentifier",
                nullable: true);
        }
    }
}
