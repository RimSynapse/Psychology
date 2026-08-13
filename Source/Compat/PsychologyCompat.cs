using System;
using System.Reflection;
using Verse;

namespace RimSynapse.Psychology
{
    /// <summary>
    /// Psychology's half of the cross-mod compatibility contract (Core #91, slice 2), and the
    /// reference implementation other companions copy.
    ///
    /// <para>It registers Psychology's facts into Core's runtime registry and probes Core's API
    /// contract — <b>all by reflection</b>, so a Core older than the registry (or, in principle, an
    /// absent Core) cannot crash the probe. If Core's contract is lower than what this build needs,
    /// it records that in <see cref="IsCoreCompatible"/> and warns, instead of dying with a
    /// <c>TypeLoadException</c> the first time it touches a changed Core API. Feature wiring that
    /// depends on the newer API should check <see cref="IsCoreCompatible"/> and degrade.</para>
    /// </summary>
    public static class PsychologyCompat
    {
        /// <summary>
        /// The Core API contract this Psychology build was compiled against. Bump it when Psychology
        /// starts relying on a newer Core API. Compared against Core's <c>CoreApiContract</c> at load.
        /// </summary>
        public const int RequiredCoreContract = 1;

        /// <summary>This mod's version, for the registry's informational report. Keep in step with About/Manifest.xml.</summary>
        public const string PsychologyVersion = "0.7.1";

        /// <summary>
        /// False when the installed Core is older than <see cref="RequiredCoreContract"/> — or absent
        /// / too old to expose the registry at all. Core-API-dependent feature wiring should check
        /// this and degrade gracefully rather than throw.
        /// </summary>
        public static bool IsCoreCompatible { get; private set; } = true;

        private const string RegistryTypeName = "RimSynapse.Compat.SynapseCompatRegistry";

        /// <summary>
        /// Register Psychology's facts with Core and probe the API contract. Reflection only, wrapped
        /// so it can never take the mod's constructor down.
        /// </summary>
        public static void RegisterAndProbe(string[] capabilities)
        {
            try
            {
                Type reg = GenTypes.GetTypeInAnyAssembly(RegistryTypeName);
                if (reg == null)
                {
                    // Core absent, or a Core that predates the compatibility registry. We cannot
                    // verify the contract, so treat it as incompatible and say so once — without throwing.
                    IsCoreCompatible = false;
                    RimSynapse.SynapseLogger.Warning(
                        "[RimSynapse-Psychology] Compat: RimSynapse Core not detected, or too old to expose the compatibility " +
                        "registry. Psychology needs Core API >= " + RequiredCoreContract + "; running in reduced-safety mode.");
                    return;
                }

                int coreContract = ReadCoreContract(reg);
                IsCoreCompatible = coreContract >= RequiredCoreContract;

                // Register our facts — primitive args only, so no Core type appears in this call.
                MethodInfo register = reg.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[]
                {
                    "rimsynapse.psychology", "RimSynapse Psychology", PsychologyVersion, RequiredCoreContract, capabilities
                });

                if (IsCoreCompatible)
                {
                    RimSynapse.SynapseLogger.Info("psychology",
                        $"[RimSynapse-Psychology] Compat: registered (Core API {coreContract} >= required {RequiredCoreContract}).");
                }
                else
                {
                    RimSynapse.SynapseLogger.Warning(
                        $"[RimSynapse-Psychology] Compat: needs Core API >= {RequiredCoreContract}, installed Core provides API " +
                        $"{coreContract}. Some features may be disabled — update RimSynapse Core.");
                }
            }
            catch (Exception e)
            {
                // The probe exists to PREVENT a hard crash on an incompatible Core; it must not become one.
                IsCoreCompatible = false;
                RimSynapse.SynapseLogger.Warning(
                    "[RimSynapse-Psychology] Compat probe failed (treating as incompatible): " + e.Message);
            }
        }

        private static int ReadCoreContract(Type reg)
        {
            FieldInfo f = reg.GetField("CoreApiContract", BindingFlags.Public | BindingFlags.Static);
            object val = f?.GetValue(null);
            return val is int i ? i : -1;
        }
    }
}
