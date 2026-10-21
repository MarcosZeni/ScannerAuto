using TallerScan.Core.Actuators;
using TallerScan.Infrastructure.Actuators;
using TallerScan.Infrastructure.Simulation;

namespace TallerScan.Tests;

// Perfiles en JSON: ida y vuelta, validación y coincidencia por VIN.
public class ProfileStoreTests
{
    private static ActuatorProfile Real() => SimulatedActuators.Profile with
    {
        Id = "prueba-real",
        Name = "Auto de prueba",
        Source = "captura de prueba",
        VinPrefixes = ["8AW", "9BW"],
    };

    [Fact]
    public void Ida_y_vuelta_en_json()
    {
        var store = new ProfileStore();
        var json = ProfileStore.Serialize(Real());
        var loaded = store.Load(json);
        Assert.Equal("prueba-real", loaded.Id);
        Assert.Equal(SimulatedActuators.Profile.Tests.Count, loaded.Tests.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), loaded.Tests.Single(t => t.Id == "electro").MaxDuration);
        Assert.True(loaded.Tests.Single(t => t.Id == "electro").StopStep!.IsStop);
        Assert.Equal(ActuatorPrecondition.EngineOff, loaded.Tests[0].Precondition);
        Assert.Contains(loaded, store.Selectable);
        Assert.DoesNotContain(SimulatedActuators.Profile, store.Selectable);
    }

    [Fact]
    public void Coincide_por_prefijo_de_vin()
    {
        var store = new ProfileStore();
        store.Load(ProfileStore.Serialize(Real()));
        Assert.Equal("prueba-real", store.Match(simulated: false, "8AWZZZ5UZEA000001")!.Id);
        Assert.Null(store.Match(simulated: false, "8AXSMLTSCAN000017"));
        Assert.Equal("simulado", store.Match(simulated: true, null)!.Id);
    }

    [Theory]
    [InlineData("27010000", "no permitido")]   // seguridad
    [InlineData("2E40010100", "no permitido")] // escritura
    [InlineData("11010000", "no permitido")]   // reset
    [InlineData("2F40", "demasiado corto")]
    [InlineData("2F4", "2 a 7 bytes")]
    public void Rechaza_servicios_fuera_de_la_lista(string payload, string expected)
    {
        var profile = Real() with
        {
            Tests = [new ActuatorTest("x", "X", "", "Motor", [new ActuatorStep("Ir", payload), new ActuatorStep("Parar", "2F400100", IsStop: true)], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(5))],
        };
        var ex = Assert.Throws<ArgumentException>(() => new ProfileStore().Load(ProfileStore.Serialize(profile)));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Rechaza_sin_paso_de_apagado_o_modulo_desconocido()
    {
        var sinStop = Real() with { Tests = [new ActuatorTest("x", "X", "", "Motor", [new ActuatorStep("Ir", "2F40010301")], ActuatorPrecondition.EngineOff, TimeSpan.FromSeconds(5))] };
        Assert.Contains("IsStop", Assert.Throws<ArgumentException>(() => new ProfileStore().Load(ProfileStore.Serialize(sinStop))).Message);

        var moduloRaro = Real() with { ModuleClears = [new ModuleClear("ABS")] };
        Assert.Contains("ABS", Assert.Throws<ArgumentException>(() => new ProfileStore().Load(ProfileStore.Serialize(moduloRaro))).Message);

        Assert.Throws<ArgumentException>(() => new ProfileStore().Load("{ esto no es json"));
        Assert.Contains("Id propio", Assert.Throws<ArgumentException>(() => new ProfileStore().Load(ProfileStore.Serialize(SimulatedActuators.Profile))).Message);
    }
}
