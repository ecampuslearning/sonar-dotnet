﻿/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2024 SonarSource SA
 * mailto: contact AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

namespace SonarAnalyzer.AnalysisContext;

public abstract class SonarReportingContextBase<TContext> : SonarAnalysisContextBase<TContext>
{
    private protected abstract ReportingContext CreateReportingContext(Diagnostic diagnostic);

    protected SonarReportingContextBase(SonarAnalysisContext analysisContext, TContext context) : base(analysisContext, context) { }

    protected void ReportIssueCore(Diagnostic diagnostic)
    {
        diagnostic = EnsureDiagnosticLocation(diagnostic);
        if (!GeneratedCodeRecognizer.IsRazorGeneratedFile(diagnostic.Location.SourceTree) // In case of Razor generated content, we don't want to raise any issues
            && HasMatchingScope(diagnostic.Descriptor)
            && SonarAnalysisContext.LegacyIsRegisteredActionEnabled(diagnostic.Descriptor, diagnostic.Location?.SourceTree))
        {
            var reportingContext = CreateReportingContext(diagnostic);
            if (reportingContext is { Compilation: { } compilation, Diagnostic.Location: { Kind: LocationKind.SourceFile, SourceTree: { } tree } } && !compilation.ContainsSyntaxTree(tree))
            {
                Debug.Fail("Primary location should be part of the compilation. An AD0001 is raised if this is not the case.");
                return;
            }
            // This is the current way SonarLint will handle how and what to report.
            if (SonarAnalysisContext.ReportDiagnostic is not null)
            {
                Debug.Assert(SonarAnalysisContext.ShouldDiagnosticBeReported == null, "Not expecting SonarLint to set both the old and the new delegates.");
                SonarAnalysisContext.ReportDiagnostic(reportingContext);
                return;
            }
            // Standalone NuGet, Scanner run and SonarLint < 4.0 used with latest NuGet
            if (!VbcHelper.IsTriggeringVbcError(reportingContext.Diagnostic)
                && (SonarAnalysisContext.ShouldDiagnosticBeReported?.Invoke(reportingContext.SyntaxTree, reportingContext.Diagnostic) ?? true))
            {
                reportingContext.ReportDiagnostic(reportingContext.Diagnostic);
            }
        }
    }

    private static Diagnostic EnsureDiagnosticLocation(Diagnostic diagnostic)
    {
        if (!GeneratedCodeRecognizer.IsRazorGeneratedFile(diagnostic.Location.SourceTree) || !diagnostic.Location.GetMappedLineSpan().HasMappedPath)
        {
            return diagnostic;
        }

        var mappedLocation = diagnostic.Location.EnsureMappedLocation();

        var descriptor = new DiagnosticDescriptor(diagnostic.Descriptor.Id,
            diagnostic.Descriptor.Title,
            diagnostic.GetMessage(),
            diagnostic.Descriptor.Category,
            diagnostic.Descriptor.DefaultSeverity,
            diagnostic.Descriptor.IsEnabledByDefault,
            diagnostic.Descriptor.Description,
            diagnostic.Descriptor.HelpLinkUri,
            diagnostic.Descriptor.CustomTags.ToArray());

        return Diagnostic.Create(descriptor,
            mappedLocation,
            diagnostic.AdditionalLocations.Select(l => l.EnsureMappedLocation()).ToImmutableList(),
            diagnostic.Properties);
    }
}

/// <summary>
/// Base class for reporting contexts that are executed on a known Tree. The decisions about generated code and unchanged files are taken during action registration.
/// </summary>
public abstract class SonarTreeReportingContextBase<TContext> : SonarReportingContextBase<TContext>
{
    public abstract SyntaxTree Tree { get; }

    protected SonarTreeReportingContextBase(SonarAnalysisContext analysisContext, TContext context) : base(analysisContext, context) { }

    [Obsolete("Use overload without Diagnostic.Create, or add one")]
    public void ReportIssue(Diagnostic diagnostic) =>
        ReportIssueCore(diagnostic);

    public void ReportIssue(DiagnosticDescriptor rule, SyntaxNode locationSyntax, params object[] messageArgs) =>
        ReportIssue(rule, locationSyntax.GetLocation(), messageArgs);

    public void ReportIssue(DiagnosticDescriptor rule, SyntaxToken locationToken, params object[] messageArgs) =>
        ReportIssue(rule, locationToken.GetLocation(), messageArgs);

    public void ReportIssue(DiagnosticDescriptor rule, Location location, params object[] messageArgs) =>
        ReportIssueCore(Diagnostic.Create(rule, location, messageArgs));
}

/// <summary>
/// Base class for reporting contexts that are common for the entire compilation. Specific tree is not known before the action is executed.
/// </summary>
public abstract class SonarCompilationReportingContextBase<TContext> : SonarReportingContextBase<TContext>
{
    protected SonarCompilationReportingContextBase(SonarAnalysisContext analysisContext, TContext context) : base(analysisContext, context) { }

    public void ReportIssue(GeneratedCodeRecognizer generatedCodeRecognizer, Diagnostic diagnostic)
    {
        if (ShouldAnalyzeTree(diagnostic.Location.SourceTree, generatedCodeRecognizer))
        {
            ReportIssueCore(diagnostic);
        }
    }
}
