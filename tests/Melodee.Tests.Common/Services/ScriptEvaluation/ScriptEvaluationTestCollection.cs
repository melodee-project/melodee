namespace Melodee.Tests.Common.Services.ScriptEvaluation;

/// <summary>
/// Collection definition to ensure script evaluation tests run sequentially
/// and don't interfere with each other due to Jint engine constraints.
/// </summary>
[CollectionDefinition("ScriptEvaluation", DisableParallelization = true)]
public class ScriptEvaluationTestCollection;
