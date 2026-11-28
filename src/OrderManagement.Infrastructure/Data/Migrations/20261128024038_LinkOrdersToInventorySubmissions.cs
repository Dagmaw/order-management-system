using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderManagement.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkOrdersToInventorySubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "OrderSubmissions",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventorySubmissionId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_InventorySubmissionId",
                table: "Orders",
                column: "InventorySubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_InventorySubmissionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "OrderSubmissions");

            migrationBuilder.DropColumn(
                name: "InventorySubmissionId",
                table: "Orders");
        }
    }
}
