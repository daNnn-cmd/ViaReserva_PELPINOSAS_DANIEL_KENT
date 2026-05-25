using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ViaReservaERP.Migrations
{
    /// <inheritdoc />
    public partial class InitialSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This is an idempotent sync migration. 
            // It ensures that even if the database was created without EF migrations (e.g. EnsureCreated), 
            // it now has the necessary columns for the updated models.
            
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Services]') AND name = 'IsDeleted') ALTER TABLE [Services] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Reservations]') AND name = 'CreatedAt') ALTER TABLE [Reservations] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Payments]') AND name = 'CreatedAt') ALTER TABLE [Payments] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Rooms]') AND name = 'IsDeleted') ALTER TABLE [Rooms] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Guests]') AND name = 'IsDeleted') ALTER TABLE [Guests] ADD [IsDeleted] BIT NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Guests]') AND name = 'IsActive') ALTER TABLE [Guests] ADD [IsActive] BIT NOT NULL DEFAULT 1;");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Guests]') AND name = 'CreatedAt') ALTER TABLE [Guests] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back an idempotent sync is usually not desired as it might break existing data paths.
        }
    }
}
