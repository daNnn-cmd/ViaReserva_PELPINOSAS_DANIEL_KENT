using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViaReservaERP.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationIdToServiceRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservationId",
                table: "ServiceRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Companies",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_ReservationId",
                table: "ServiceRequests",
                column: "ReservationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceRequests_Reservations_ReservationId",
                table: "ServiceRequests",
                column: "ReservationId",
                principalTable: "Reservations",
                principalColumn: "ReservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceRequests_Reservations_ReservationId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_ReservationId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Companies");
        }
    }
}
