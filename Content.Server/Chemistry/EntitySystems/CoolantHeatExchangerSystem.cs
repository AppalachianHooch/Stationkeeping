using Content.Server.Atmos.EntitySystems;
using Content.Server.Chemistry.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos.Components;
using Content.Shared.FixedPoint;
using Content.Shared.NodeContainer;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class CoolantHeatExchangerSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;

    // Scales coolant K-drop into atmos joules; tuned to BleedHeat neighbourhood.
    private const float HeatDumpScale = 8f;
    private const float CoolantAmbientTemp = 293.15f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CoolantHeatExchangerComponent, AtmosDeviceUpdateEvent>(OnAtmosUpdate);
    }

    private void OnAtmosUpdate(EntityUid uid, CoolantHeatExchangerComponent comp, ref AtmosDeviceUpdateEvent args)
    {
        if (!_nodeContainer.TryGetNode<ReagentPipeNode>(uid, comp.NodeName, out var node)
            || node.NodeGroup is not ReagentPipeNet net
            || net.Reagents.Volume <= FixedPoint2.Zero
            || net.Temperature <= CoolantAmbientTemp)
            return;

        var excess = net.Temperature - CoolantAmbientTemp;
        var shed = comp.ExchangeRate * excess * args.dt;
        net.Temperature = MathF.Max(CoolantAmbientTemp, net.Temperature - shed);

        var xform = Transform(uid);
        var mixture = _atmos.GetTileMixture((uid, xform), excite: true);
        if (mixture == null)
            return;

        var heatCapacity = _atmos.GetHeatCapacity(mixture, true);
        if (heatCapacity > 0f)
            _atmos.AddHeat(mixture, shed * HeatDumpScale * heatCapacity);
    }
}
