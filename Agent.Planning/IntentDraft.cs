// Sprint 39 P1-C: IntentDraft moved to Agent.Core so that IAgentRuntimeComponent
// (Agent.Core.Runtime) can reference it without a circular project dependency.
// All Agent.Planning code reaches it via the existing "using Agent.Core;" directive.
// See Agent.Core/Models/IntentDraft.cs for the full definition.
