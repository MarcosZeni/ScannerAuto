using TallerScan.Core.Diagnostics;
using TallerScan.Core.Safety;
using TallerScan.Infrastructure.Elm;

namespace TallerScan.Tests;

public class DecodingTests
{
    private static double Decode(string message)
    {
        var bytes = ElmTextParser.ParseHex(message)!;
        Assert.True(PidCatalog.TryDecode(bytes[1], bytes.AsSpan(2), out var v));
        return v;
    }

    // Vectores de la sección 15 del plan.
    [Theory]
    [InlineData("41 0C 1A F8", 1726)]
    [InlineData("41 0C 00 00", 0)]
    [InlineData("41 05 7B", 83)]
    [InlineData("41 0D 28", 40)]
    [InlineData("41 10 01 F4", 5)]
    [InlineData("41 06 80", 0)]
    [InlineData("41 42 36 B0", 14)]
    public void Vectores_del_plan(string message, double expected) =>
        Assert.Equal(expected, Decode(message), 3);

    [Fact]
    public void Respuesta_incompleta_no_da_valor()
    {
        var bytes = ElmTextParser.ParseHex("41 0C 1A")!;
        Assert.False(PidCatalog.TryDecode(0x0C, bytes.AsSpan(2), out _));
    }

    [Fact]
    public void Mascara_de_soporte_orientacion_de_bits()
    {
        // BE 1F A8 13: ejemplo clásico de 01 00.
        var set = SupportMask.Parse(0x00, [0xBE, 0x1F, 0xA8, 0x13]);
        Assert.Contains((byte)0x01, set);
        Assert.DoesNotContain((byte)0x02, set);
        Assert.Contains((byte)0x0C, set);
        Assert.Contains((byte)0x0D, set);
        Assert.Contains((byte)0x20, set); // último bit: hay otro bloque
        Assert.Contains((byte)0x1F, set);
        Assert.DoesNotContain((byte)0x1E, set);
    }

    [Fact]
    public void Mascara_extremos_y_ida_vuelta()
    {
        var set = SupportMask.Parse(0x20, [0x80, 0x00, 0x00, 0x01]);
        Assert.Equal([(byte)0x21, (byte)0x40], set.Order().ToArray());
        Assert.Equal(new byte[] { 0x80, 0, 0, 1 }, SupportMask.Encode(0x20, set));
    }

    [Theory]
    [InlineData(0x03, 0x01, "P0301")]
    [InlineData(0x04, 0x20, "P0420")]
    [InlineData(0x41, 0x23, "C0123")]
    [InlineData(0x81, 0x00, "B0100")]
    [InlineData(0xC1, 0x00, "U0100")]
    [InlineData(0x12, 0x34, "P1234")]
    public void Dtc_ida_y_vuelta(byte a, byte b, string code)
    {
        Assert.Equal(code, DtcCodec.Decode(a, b));
        Assert.Equal((a, b), DtcCodec.Encode(code));
    }

    [Fact]
    public void Relleno_no_es_P0000() => Assert.Null(DtcCodec.Decode(0, 0));

    [Fact]
    public void Catalogo_describe_genericos_y_no_inventa_de_fabricante()
    {
        Assert.Contains("catalizador", DtcCatalog.Describe("P0420").Description!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cilindro 3", DtcCatalog.Describe("P0303").Description!);
        Assert.Contains("cilindro 12", DtcCatalog.Describe("P0312").Description!);
        var maker = DtcCatalog.Describe("P1234");
        Assert.Null(maker.Description);
        Assert.False(maker.IsGeneric);
    }

    [Fact]
    public void Monitores_decodifica_mil_y_cantidad()
    {
        var m = MonitorDecoder.Decode(Core.Models.EcuOrigin.Unknown, [0x82, 0x07, 0x65, 0x01]);
        Assert.True(m.MilOn);
        Assert.Equal(2, m.DtcCount);
        Assert.False(m.IsDiesel);
        var cat = m.Monitors.Single(x => x.Name == "Catalizador");
        Assert.True(cat.Available);
        Assert.False(cat.Complete);
    }

    [Theory]
    [InlineData("0100", true)]
    [InlineData("010C", true)]
    [InlineData("03", true)]
    [InlineData("07", true)]
    [InlineData("0A", true)]
    [InlineData("0902", true)]
    [InlineData("020C00", true)]
    [InlineData("ATZ", true)]
    [InlineData("ATST32", true)]
    [InlineData("04", false)]
    [InlineData("0801", false)]
    [InlineData("1003", false)]
    [InlineData("2E1234", false)]
    [InlineData("ATMA", false)]
    [InlineData("ATSH7E0", false)]
    [InlineData("", false)]
    [InlineData("010C\r04", false)]
    [InlineData("01 0C", false)]
    [InlineData("010c", false)]
    public void Politica_de_comandos(string command, bool allowed) =>
        Assert.Equal(allowed, CommandPolicy.IsAllowed(command, out _));
}
