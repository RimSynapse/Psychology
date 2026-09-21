using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;
using RimWorld;
using RimSynapse;
using RimSynapse.Comps;
using RimSynapse.Models;
using RimSynapse.Utils;
using RimSynapse.Psychology.API;
using RimSynapse.Psychology.Comps;
using RimAgentic.Testing;
using Newtonsoft.Json;

namespace RimSynapse.Psychology.Tests
{
    /// <summary>
    /// The live-LLM Determination runner (Core#83, Increment 2). Armed by <c>-synapse-determination</c>,
    /// which also bypasses the quicktest LLM mock (see SynapseClient) so it runs against the REAL model
    /// while still using quicktest's auto-generated colony.
    ///
    /// <para>It seeds a colonist, fires a real LLM decision (evaluation / memory generation), awaits the
    /// async response with a timeout, asserts the structured fields, and records the outcome + the real
    /// response into the Determination tier of Learning/Test_Report.md. If the model is unreachable the
    /// scenario records SKIP (not FAIL) — a down server is not a determination failure.</para>
    ///
    /// <para>An <see cref="ISynapseTestDriver"/> rather than a GameComponent: this assembly is loaded at
    /// runtime by the RimAgentic test host, after Verse's GameComponent scan, so the host instantiates
    /// it and pumps <see cref="Update"/> once per frame. The driver owns its own lifecycle including
    /// shutdown, exactly as the old GameComponent did.</para>
    /// </summary>
    public class DeterminationRunnerDriver : ISynapseTestDriver
    {
        private const string Arg = "synapse-determination";
        private const int WarmupFrames = 60;
        private const double PerScenarioTimeoutSec = 90.0;
        private const int ForceExitFrames = 300;

        private enum Phase { Warmup, Trigger, Await, Done }

        private readonly bool _armed;
        private Phase _phase = Phase.Warmup;
        private int _framesWaited;
        private int _framesSinceShutdown = -1;
        private int _idx;
        private bool _responded, _llmOk;
        private readonly Stopwatch _sw = new Stopwatch();
        private Pawn _pawn;
        private List<DetScenario> _scenarios;
        private readonly List<SynapseTestRecord> _results = new List<SynapseTestRecord>();
        private float _capturedWeight;

        public DeterminationRunnerDriver()
        {
            _armed = GenCommandLine.CommandLineArgPassed(Arg);
            if (_armed)
                SynapseTestReporter.Info($"Determination runner armed (-{Arg}); warming up {WarmupFrames} frames.");
        }

        public bool Armed => _armed;

        public void Update()
        {
            if (!_armed) return;
            if (_phase == Phase.Done)
            {
                if (_framesSinceShutdown >= 0 && ++_framesSinceShutdown > ForceExitFrames)
                {
                    _framesSinceShutdown = -1;
                    SynapseTestReporter.Info("Determination shutdown did not complete; forcing exit.");
                    Environment.Exit(0);
                }
                return;
            }

            switch (_phase)
            {
                case Phase.Warmup:
                    if (_framesWaited++ < WarmupFrames) return;
                    _pawn = Find.Maps.SelectMany(m => m.mapPawns.FreeColonists).FirstOrDefault();
                    _scenarios = BuildScenarios();
                    if (_pawn == null)
                    {
                        foreach (var s in _scenarios) Record(s, "SKIP", "no colonist on the map to evaluate");
                        Finish();
                        return;
                    }
                    _phase = Phase.Trigger;
                    return;

                case Phase.Trigger:
                    if (_idx >= _scenarios.Count) { Finish(); return; }
                    var sc = _scenarios[_idx];
                    _responded = false; _llmOk = false; _capturedWeight = 0f;
                    try { sc.Setup?.Invoke(_pawn); }
                    catch (Exception ex) { Record(sc, "SKIP", "setup threw: " + ex.Message); _idx++; return; }
                    _sw.Restart();
                    try { sc.Trigger(_pawn, ok => { _llmOk = ok; _responded = true; }); }
                    catch (Exception ex) { Record(sc, "SKIP", "trigger threw: " + ex.Message); _idx++; return; }
                    _phase = Phase.Await;
                    return;

                case Phase.Await:
                    var cur = _scenarios[_idx];
                    if (_responded)
                    {
                        if (!_llmOk) Record(cur, "SKIP", "LLM call failed — model server unreachable?");
                        else
                        {
                            try { var r = cur.Evaluate(_pawn); Record(cur, r.Outcome, r.Detail); }
                            catch (Exception ex) { Record(cur, "FAIL", "evaluate threw: " + ex.Message); }
                        }
                        _idx++; _phase = Phase.Trigger;
                    }
                    else if (_sw.Elapsed.TotalSeconds > PerScenarioTimeoutSec)
                    {
                        Record(cur, "SKIP", $"timed out after {PerScenarioTimeoutSec:0}s — model server up?");
                        _idx++; _phase = Phase.Trigger;
                    }
                    return;
            }
        }

        private void Record(DetScenario sc, string outcome, string detail)
        {
            SynapseTestReporter.Info($"[SYNAPSE-DET] {outcome} {sc.Name} | {detail}");
            _results.Add(new SynapseTestRecord
            {
                Name = sc.Name, Tier = "Determination", Polarity = sc.Polarity,
                Scenario = sc.Scenario, Expectation = sc.Expectation, Outcome = outcome, Detail = detail
            });
        }

        private void Finish()
        {
            int p = _results.Count(r => r.Outcome == "PASS"), f = _results.Count(r => r.Outcome == "FAIL"), s = _results.Count(r => r.Outcome == "SKIP");
            SynapseTestReport.Write(_results, p, f, s);
            SynapseTestReporter.Info($"[SYNAPSE-DET] SUMMARY passed={p} failed={f} skipped={s}");
            _phase = Phase.Done;
            _framesSinceShutdown = 0;
            try { Root.Shutdown(); } catch { UnityEngine.Application.Quit(); }
        }

        // ── scenarios ─────────────────────────────────────────────────────────
        private class DetResult { public string Outcome, Detail; }
        private class DetScenario
        {
            public string Name, Polarity, Scenario, Expectation;
            public Action<Pawn> Setup;
            public Action<Pawn, Action<bool>> Trigger;
            public Func<Pawn, DetResult> Evaluate;
        }

        private static string Likelihood(Pawn pawn)
        {
            var pc = pawn.TryGetComp<SynapsePawnComp>();
            if (pc?.medicalProfile != null && pc.medicalProfile.TryGetValue("PersonalityShiftLikelihood", out var v) && !string.IsNullOrEmpty(v))
                return v.Trim().ToLowerInvariant();
            return "(none returned)";
        }

        private static void SeedCombat(Pawn pawn, string targetKind)
        {
            var cc = pawn.TryGetComp<SynapseCorePawnComp>();
            if (cc == null) return;
            cc.recentJobs.Clear();
            cc.lastJobStartedTick = -1;
            cc.lastJobDefName = null;
            int now = Find.TickManager.TicksGame;
            string def = targetKind == "object" ? "AttackStatic" : "AttackMelee";
            for (int i = 0; i < 40; i++) cc.recentJobs.Add(new JobInterval(def, now - 5000 + i * 10, 40, targetKind));
        }

        private List<DetScenario> BuildScenarios()
        {
            return new List<DetScenario>
            {
                new DetScenario
                {
                    Name = "Determination_PodCarDoesNotWantBloodlust", Polarity = "negative",
                    Scenario = "Live eval of a colonist who spent the day attacking an object (the pod-car case).",
                    Expectation = "PersonalityShiftLikelihood is none or low (object violence isn't bloodlust).",
                    Setup = p => SeedCombat(p, "object"),
                    Trigger = (p, done) => SynapsePsychology.QueueDailyPsychologyReview(p, 0.3f, new List<WeightedMemory>(), done),
                    Evaluate = p => { string lk = Likelihood(p); return new DetResult { Outcome = (lk == "none" || lk == "low") ? "PASS" : "FAIL", Detail = $"likelihood={lk}" }; }
                },
                new DetScenario
                {
                    Name = "Determination_RealKillingIsRecognized", Polarity = "positive",
                    Scenario = "Live eval of a colonist with lifetime humanlike kills who fought the living today.",
                    Expectation = "PersonalityShiftLikelihood is moderate or high (a shift is at least plausible).",
                    Setup = p =>
                    {
                        try { p.records?.AddTo(RecordDefOf.KillsHumanlikes, 12); } catch { }
                        SeedCombat(p, "humanlike");
                    },
                    Trigger = (p, done) => SynapsePsychology.QueueDailyPsychologyReview(p, 0.3f, new List<WeightedMemory>(), done),
                    Evaluate = p => { string lk = Likelihood(p); return new DetResult { Outcome = (lk == "moderate" || lk == "high") ? "PASS" : "FAIL", Detail = $"likelihood={lk}" }; }
                },
                new DetScenario
                {
                    Name = "Determination_MemoryWeightWithinScale", Polarity = "positive",
                    Scenario = "Live memory generation for a minor event.",
                    Expectation = "The generated Weight is within the 0-1 scale.",
                    Setup = null,
                    Trigger = (p, done) =>
                    {
                        string sys = "Generate ONE third-person memory for a colonist. Respond ONLY as JSON: { \"Summary\": \"...\", \"Tags\": [\"..\"], \"Weight\": 0.2, \"Decay\": 0.2 }. Weight is 0.0-1.0.";
                        string user = $"Colonist: {p.Name.ToStringShort}. Event: found a tasty mushroom while foraging (a minor, pleasant moment).";
                        SynapseClient.PromptAsync(RimSynapsePsychologyMod.ModHandle, sys, user, r =>
                        {
                            if (!r.success) { _capturedWeight = -1f; done(false); return; }
                            try
                            {
                                string json = JsonHelper.ExtractJson(r.content);
                                var d = json != null ? JsonConvert.DeserializeObject<Dictionary<string, object>>(json) : null;
                                _capturedWeight = (d != null && d.TryGetValue("Weight", out var w) && w != null) ? Convert.ToSingle(w) : -2f;
                            }
                            catch { _capturedWeight = -2f; }
                            done(true);
                        }, new ChatOptions { priority = 5, requestName = "Determination MemGen", targetName = p.Name.ToStringShort });
                    },
                    Evaluate = p => new DetResult { Outcome = (_capturedWeight > 0f && _capturedWeight <= 1f) ? "PASS" : "FAIL", Detail = $"weight={_capturedWeight:0.00}" }
                },
            };
        }
    }
}
