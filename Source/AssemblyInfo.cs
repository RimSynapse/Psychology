using System.Runtime.CompilerServices;

// The in-game test project (RimSynapsePsychologyTests, built into TestAssemblies/) exercises
// internal seams that are intentionally not part of the public mod API — e.g. the #73 event
// involvement-roster resolvers (SynapsePsychology.ResolveSubjectLoadIds / ResolveWitnessLoadIds).
[assembly: InternalsVisibleTo("RimSynapsePsychologyTests")]
