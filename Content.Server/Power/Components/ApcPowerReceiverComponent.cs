using Content.Server.Power.NodeGroups;
using Content.Server.Power.Pow3r;
using Content.Shared.Power.Components;

namespace Content.Server.Power.Components
{
    /// <summary>
    ///     Attempts to link with a nearby <see cref="ApcPowerProviderComponent"/>s
    ///     so that it can receive power from a <see cref="IApcNet"/>.
    /// </summary>
    [RegisterComponent]
    public sealed partial class ApcPowerReceiverComponent : SharedApcPowerReceiverComponent
    {
        /// <summary>
        ///     Amount of charge this needs from an APC per second to function.
        /// </summary>
        [DataField("powerLoad")]
        public override float Load
        {
            get => _requestedLoad;
            set
            {
                _requestedLoad = value;
                UpdateEffectiveLoad();
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        private float _requestedLoad = 5f;

        public ApcPowerProviderComponent? Provider = null;

        /// <summary>
        ///     When false, causes this to appear powered even if not receiving power from an Apc.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public override bool NeedsPower
        {
            get => _needsPower;
            set
            {
                _needsPower = value;
            }
        }

        [DataField("needsPower")]
        private bool _needsPower = true;

        /// <summary>
        ///     When true, causes this to never appear powered.
        /// </summary>
        [DataField("powerDisabled")]
        public override bool PowerDisabled
        {
            get => !NetworkLoad.Enabled;
            set => NetworkLoad.Enabled = !value;
        }

        [DataField("loadPriority")]
        [ViewVariables(VVAccess.ReadWrite)]
        public override ApcPowerPriority LoadPriority { get; set; } = ApcPowerPriority.Equipment;

        [DataField("powerOffThreshold")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float PowerOffThreshold = 1f;

        [DataField("powerOnThreshold")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float PowerOnThreshold = 1f;

        [ViewVariables(VVAccess.ReadWrite)]
        public override float ShedRatio
        {
            get => _shedRatio;
            set
            {
                _shedRatio = Math.Clamp(value, 0f, 1f);
                UpdateEffectiveLoad();
            }
        }

        private float _shedRatio = 1f;

        [ViewVariables(VVAccess.ReadWrite)]
        public ApcPowerPriorityOverride PriorityOverride = ApcPowerPriorityOverride.Auto;

        [ViewVariables]
        public ApcPowerTierState ShedState = ApcPowerTierState.Full;

        [ViewVariables]
        public PowerState.Load NetworkLoad { get; } = new PowerState.Load
        {
            DesiredPower = 5
        };

        public override float ReceivingPower
        {
            get => NetworkLoad.ReceivingPower;
            set => NetworkLoad.ReceivingPower = value;
        }

        public float PowerReceived => ReceivingPower;

        [ViewVariables]
        public float LastReceivingPower;

        private void UpdateEffectiveLoad()
        {
            NetworkLoad.DesiredPower = Math.Max(0f, _requestedLoad * _shedRatio);
        }
    }
}
