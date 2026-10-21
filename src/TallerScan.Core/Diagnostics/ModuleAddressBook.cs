using TallerScan.Core.Actuators;

namespace TallerScan.Core.Diagnostics;

// Direcciones a las que se les pregunta "¿estás ahí?" en el barrido de módulos, y qué se
// sabe de cada una sin conocer la marca. Lo único normalizado es ISO 15765-4: pedidos
// 7E0–7E7 con respuesta +8 (11 bits) y 18DAxxF1 / 18DAF1xx (29 bits). El resto de las
// direcciones las elige cada fabricante; por eso el barrido prueba todo el rango 600–7FF
// y toma como respuesta lo que conteste desde cualquier cabecera 6xx/7xx.
public static class ModuleAddressBook
{
    // Pedido funcional de OBD2: no es un módulo.
    public const string Functional11 = "7DF";

    // Dirección del equipo de diagnóstico en 29 bits (ISO 15765-4).
    public const string Tester29 = "F1";

    public static bool IsObdRequest11(int id) => id is >= 0x7E0 and <= 0x7E7;

    // Respuesta esperada cuando el adaptador no acepta un filtro amplio: la convención +8.
    public static string ExpectedResponse11(int request) => (request + 8).ToString("X3");

    public static string Request29(byte target) => $"18DA{target:X2}{Tester29}";

    public static string Response29(byte target) => $"18DA{Tester29}{target:X2}";

    // Orden del barrido en 11 bits: primero las direcciones OBD (donde están motor y caja),
    // después 700–7FF y por último 600–6FF. Se saltean el pedido funcional y las direcciones
    // de respuesta OBD (7E8–7EF), que ningún módulo usa como pedido.
    public static IReadOnlyList<int> Requests11()
    {
        var list = new List<int>();
        for (int id = 0x7E0; id <= 0x7E7; id++)
            list.Add(id);
        for (int id = 0x700; id <= 0x7FF; id++)
        {
            if (id == 0x7DF || id is >= 0x7E0 and <= 0x7EF)
                continue;
            list.Add(id);
        }
        for (int id = 0x600; id <= 0x6FF; id++)
            list.Add(id);
        return list;
    }

    // 29 bits: las 256 direcciones físicas posibles, empezando por las habituales de motor y caja.
    public static IReadOnlyList<byte> Targets29()
    {
        var list = new List<byte> { 0x10, 0x18 };
        for (int t = 0; t <= 0xFF; t++)
            if (t is not (0x10 or 0x18) && t != 0x33 && t != 0xF1)
                list.Add((byte)t);
        return list;
    }

    // Lo que se puede decir de una dirección sin conocer la marca.
    public static string? Hint(ModuleAddress address)
    {
        if (address.Is29Bit)
        {
            return address.RequestId[4..6] switch
            {
                "10" => "Motor (dirección habitual)",
                "18" => "Caja (dirección habitual)",
                _ => null,
            };
        }
        return address.RequestId switch
        {
            "7E0" => "Motor (dirección OBD estándar)",
            "7E1" => "Caja u otro módulo de emisiones (dirección OBD estándar)",
            "7E2" or "7E3" or "7E4" or "7E5" or "7E6" or "7E7" => "Módulo de emisiones (dirección OBD estándar)",
            _ => null,
        };
    }
}
