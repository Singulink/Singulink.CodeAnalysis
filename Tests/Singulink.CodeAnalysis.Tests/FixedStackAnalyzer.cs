using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

// A test creates the wrapper itself, and the analyzer that it wraps configures concurrent execution and generated code and registers
// its actions through the contexts that the wrapper hands it: the rules for an analyzer that a host loads do not apply. The compiler
// of each target of the tests declares the kinds of some registrations with 'params' or without it, and one override serves both.
#pragma warning disable RS1001, RS1012, RS1025, RS1026, RCS1193

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Runs every action of an analyzer on a thread of its own with a stack of a fixed size, and waits for it. How deeply nested code can be
/// before the analyzer reports a member that it cannot check (SCA1048) depends on how much stack is left on the thread that runs it, and
/// the testing library runs the analysis on whatever thread the thread pool gives it. With this wrapper a test of deep code gets the same
/// stack every time, whatever thread the test, the testing library and the compiler run on.
/// </summary>
/// <remarks>
/// The compiler makes the operations of a member before the analyzer is called, on the thread of the testing library, so the stack that
/// the compiler needs is not given here: a test keeps its code shallow enough for the compiler on any thread.
/// </remarks>
internal sealed class FixedStackAnalyzer(DiagnosticAnalyzer inner, int stackSize) : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => inner.SupportedDiagnostics;

    public override void Initialize(AnalysisContext context) => inner.Initialize(new FixedStackAnalysisContext(context, stackSize));

    /// <summary>
    /// Runs an action on a new thread with the given stack size, waits for it, and throws what it threw.
    /// </summary>
    private static void Run(int stackSize, Action action)
    {
        ExceptionDispatchInfo? thrown = null;

        var thread = new Thread(
            () => {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    thrown = ExceptionDispatchInfo.Capture(exception);
                }
            },
            stackSize);

        thread.Start();
        thread.Join();
        thrown?.Throw();
    }

    private static Action<T> OnFixedStack<T>(int stackSize, Action<T> action) => context => Run(stackSize, () => action(context));

    private sealed class FixedStackAnalysisContext(AnalysisContext context, int stackSize) : AnalysisContext
    {
        public override void EnableConcurrentExecution() => context.EnableConcurrentExecution();

        public override void ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags analysisMode) => context.ConfigureGeneratedCodeAnalysis(analysisMode);

        public override void RegisterCompilationStartAction(Action<CompilationStartAnalysisContext> action) =>
            context.RegisterCompilationStartAction(start => action(new FixedStackCompilationStartContext(start, stackSize)));

        public override void RegisterCompilationAction(Action<CompilationAnalysisContext> action) =>
            context.RegisterCompilationAction(OnFixedStack(stackSize, action));

        public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action) =>
            context.RegisterSemanticModelAction(OnFixedStack(stackSize, action));

        public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds) =>
            context.RegisterSymbolAction(OnFixedStack(stackSize, action), symbolKinds);

        public override void RegisterSymbolStartAction(Action<SymbolStartAnalysisContext> action, SymbolKind symbolKind) =>
            context.RegisterSymbolStartAction(OnFixedStack(stackSize, action), symbolKind);

        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action) =>
            context.RegisterCodeBlockStartAction(OnFixedStack(stackSize, action));

        public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action) =>
            context.RegisterCodeBlockAction(OnFixedStack(stackSize, action));

        public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action) =>
            context.RegisterSyntaxTreeAction(OnFixedStack(stackSize, action));

        public override void RegisterAdditionalFileAction(Action<AdditionalFileAnalysisContext> action) =>
            context.RegisterAdditionalFileAction(OnFixedStack(stackSize, action));

        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds) =>
            context.RegisterSyntaxNodeAction(OnFixedStack(stackSize, action), syntaxKinds);

        public override void RegisterOperationAction(Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds) =>
            context.RegisterOperationAction(OnFixedStack(stackSize, action), operationKinds);

        public override void RegisterOperationBlockStartAction(Action<OperationBlockStartAnalysisContext> action) =>
            context.RegisterOperationBlockStartAction(OnFixedStack(stackSize, action));

        public override void RegisterOperationBlockAction(Action<OperationBlockAnalysisContext> action) =>
            context.RegisterOperationBlockAction(OnFixedStack(stackSize, action));
    }

    // The actions that the analyzer registers when a compilation starts. An action that a start action registers in turn, as the end of
    // an operation block, runs where the compiler calls it.
    private sealed class FixedStackCompilationStartContext(CompilationStartAnalysisContext context, int stackSize)
        : CompilationStartAnalysisContext(context.Compilation, context.Options, context.CancellationToken)
    {
        public override void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action) =>
            context.RegisterCompilationEndAction(OnFixedStack(stackSize, action));

        public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action) =>
            context.RegisterSemanticModelAction(OnFixedStack(stackSize, action));

        public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds) =>
            context.RegisterSymbolAction(OnFixedStack(stackSize, action), symbolKinds);

        public override void RegisterSymbolStartAction(Action<SymbolStartAnalysisContext> action, SymbolKind symbolKind) =>
            context.RegisterSymbolStartAction(OnFixedStack(stackSize, action), symbolKind);

        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action) =>
            context.RegisterCodeBlockStartAction(OnFixedStack(stackSize, action));

        public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action) =>
            context.RegisterCodeBlockAction(OnFixedStack(stackSize, action));

        public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action) =>
            context.RegisterSyntaxTreeAction(OnFixedStack(stackSize, action));

        public override void RegisterAdditionalFileAction(Action<AdditionalFileAnalysisContext> action) =>
            context.RegisterAdditionalFileAction(OnFixedStack(stackSize, action));

        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds) =>
            context.RegisterSyntaxNodeAction(OnFixedStack(stackSize, action), syntaxKinds);

        public override void RegisterOperationAction(Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds) =>
            context.RegisterOperationAction(OnFixedStack(stackSize, action), operationKinds);

        public override void RegisterOperationBlockStartAction(Action<OperationBlockStartAnalysisContext> action) =>
            context.RegisterOperationBlockStartAction(OnFixedStack(stackSize, action));

        public override void RegisterOperationBlockAction(Action<OperationBlockAnalysisContext> action) =>
            context.RegisterOperationBlockAction(OnFixedStack(stackSize, action));
    }
}
