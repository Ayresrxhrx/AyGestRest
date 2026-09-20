using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AyGestRest.Migrations
{
    /// <inheritdoc />
    public partial class AddHorarioUsuarioColumnsAndFuncionarioTurnoUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                table: "HorarioUsuarios",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "DomingoEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "DomingoSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "QuartaEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "QuartaSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "QuintaEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "QuintaSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SabadoEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SabadoSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SegundaEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SegundaSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SextaEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "SextaSida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TercaEntrada",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TercaSaida",
                table: "HorarioUsuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "Atraso",
                table: "FuncionarioTurnos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HoraExtraAutorizada",
                table: "FuncionarioTurnos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "HorasTrabalhadas",
                table: "FuncionarioTurnos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ativo",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "DomingoEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "DomingoSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "QuartaEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "QuartaSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "QuintaEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "QuintaSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SabadoEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SabadoSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SegundaEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SegundaSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SextaEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "SextaSida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "TercaEntrada",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "TercaSaida",
                table: "HorarioUsuarios");

            migrationBuilder.DropColumn(
                name: "Atraso",
                table: "FuncionarioTurnos");

            migrationBuilder.DropColumn(
                name: "HoraExtraAutorizada",
                table: "FuncionarioTurnos");

            migrationBuilder.DropColumn(
                name: "HorasTrabalhadas",
                table: "FuncionarioTurnos");
        }
    }
}
