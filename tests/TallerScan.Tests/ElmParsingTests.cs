using TallerScan.Core.Models;
using TallerScan.Infrastructure.Elm;

namespace TallerScan.Tests;

public class ElmParsingTests
{
    private static readonly ObdProtocol Can11 = new('6', "CAN 11");
    private static readonly ObdProtocol Can29 = new('7', "CAN 29");
    private static readonly ObdProtocol Iso9141 = new('3', "ISO 9141-2");

    [Fact]
    public void Quita_eco_searching_y_saltos()
    {
        var r = ElmTextParser.Parse("010C", "010C\r\nSEARCHING...\r\n41 0C 1A F8 \r\n\r\n", 5, null);
        Assert.Equal(ReadStatus.Success, r.Status);
        Assert.Equal(["41 0C 1A F8"], r.Lines);
    }

    [Theory]
    [InlineData("NO DATA", ReadStatus.NoData)]
    [InlineData("SEARCHING...\rUNABLE TO CONNECT", ReadStatus.CommunicationError)]
    [InlineData("CAN ERROR", ReadStatus.CommunicationError)]
    [InlineData("BUS INIT: ...ERROR", ReadStatus.CommunicationError)]
    [InlineData("STOPPED", ReadStatus.CommunicationError)]
    [InlineData("BUFFER FULL", ReadStatus.CommunicationError)]
    [InlineData("?", ReadStatus.InvalidResponse)]
    [InlineData("ERR94", ReadStatus.CommunicationError)]
    [InlineData("41 0C 1A F8 <DATA ERROR", ReadStatus.InvalidResponse)]
    public void Errores_del_adaptador_no_son_datos(string raw, ReadStatus expected) =>
        Assert.Equal(expected, ElmTextParser.Parse("010C", raw, 0, null).Status);

    [Fact]
    public void Mismo_pid_dos_emisores_no_se_pisan()
    {
        // Fixture del plan: 7E8 y 7E9 responden RPM.
        var a = FrameAssembler.Assemble(["7E8 04 41 0C 1A F8 00 00 00", "7E9 04 41 0C 0F A0 00 00 00"], Can11, true);
        Assert.Empty(a.Problems);
        Assert.Equal(2, a.Messages.Count);
        Assert.Equal("7E8", a.Messages[0].Address);
        Assert.Equal(new byte[] { 0x41, 0x0C, 0x1A, 0xF8 }, a.Messages[0].Payload);
        Assert.Equal("7E9", a.Messages[1].Address);
        Assert.Equal(new byte[] { 0x41, 0x0C, 0x0F, 0xA0 }, a.Messages[1].Payload);
    }

    [Fact]
    public void Dtc_en_trama_unica_can()
    {
        var a = FrameAssembler.Assemble(["7E8 06 43 02 03 01 04 20 00"], Can11, true);
        var codes = new List<string>();
        Assert.True(Elm327Backend.TryDecodeDtcs(a.Messages.Single().Payload, legacy: false, codes));
        Assert.Equal(["P0301", "P0420"], codes);
    }

    [Fact]
    public void Dtc_cantidad_incoherente_es_invalida()
    {
        var codes = new List<string>();
        Assert.False(Elm327Backend.TryDecodeDtcs([0x43, 0x03, 0x03, 0x01], legacy: false, codes));
    }

    [Fact]
    public void Dtc_k_line_tres_por_linea_con_relleno()
    {
        var codes = new List<string>();
        Assert.True(Elm327Backend.TryDecodeDtcs([0x43, 0x01, 0x30, 0x00, 0x00, 0x00, 0x00], legacy: true, codes));
        Assert.Equal(["P0130"], codes);
    }

    [Fact]
    public void Vin_multitrama_can()
    {
        var lines = new[]
        {
            "7E8 10 14 49 02 01 38 41 58",
            "7E8 21 53 4D 4C 54 53 43 41",
            "7E8 22 4E 30 30 30 30 31 37",
        };
        var a = FrameAssembler.Assemble(lines, Can11, true);
        Assert.Empty(a.Problems);
        var payload = a.Messages.Single().Payload;
        Assert.Equal(0x14, payload.Length);
        Assert.Equal("8AXSMLTSCAN000017", System.Text.Encoding.ASCII.GetString(payload[3..]));
    }

    [Fact]
    public void Secuencia_rota_invalida_el_mensaje()
    {
        var lines = new[] { "7E8 10 14 49 02 01 38 41 58", "7E8 22 4E 30 30 30 30 31 37" };
        var a = FrameAssembler.Assemble(lines, Can11, true);
        Assert.Empty(a.Messages);
        Assert.NotEmpty(a.Problems);
    }

    [Fact]
    public void Mensaje_largo_incompleto_no_se_entrega()
    {
        var a = FrameAssembler.Assemble(["7E8 10 14 49 02 01 38 41 58"], Can11, true);
        Assert.Empty(a.Messages);
        Assert.Single(a.Problems);
    }

    [Fact]
    public void Can_29_bits()
    {
        var a = FrameAssembler.Assemble(["18 DA F1 10 04 41 0C 1A F8 00 00 00"], Can29, true);
        Assert.Equal("18DAF110", a.Messages.Single().Address);
        Assert.Equal(new byte[] { 0x41, 0x0C, 0x1A, 0xF8 }, a.Messages[0].Payload);
    }

    [Fact]
    public void K_line_con_checksum()
    {
        // 48+6B+10+41+0C+1A+F8 = 0x222 → checksum 22
        var ok = FrameAssembler.Assemble(["48 6B 10 41 0C 1A F8 22"], Iso9141, true);
        Assert.Equal("10", ok.Messages.Single().Address);
        Assert.Equal(new byte[] { 0x41, 0x0C, 0x1A, 0xF8 }, ok.Messages[0].Payload);

        var bad = FrameAssembler.Assemble(["48 6B 10 41 0C 1A F8 23"], Iso9141, true);
        Assert.Empty(bad.Messages);
    }

    [Fact]
    public void Sin_cabeceras_formato_largo_del_elm()
    {
        var lines = new[] { "014", "0: 49 02 01 38 41 58", "1: 53 4D 4C 54 53 43 41", "2: 4E 30 30 30 30 31 37" };
        var a = FrameAssembler.Assemble(lines, Can11, false);
        var m = a.Messages.Single();
        Assert.Null(m.Address);
        Assert.Equal(0x14, m.Payload.Length);
    }
}
