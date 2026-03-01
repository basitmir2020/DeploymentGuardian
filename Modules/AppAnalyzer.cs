using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class AppAnalyzer : IAnalyzer<AppAnalysisResult>
{
    /// <summary>
    /// Returns a static application profile used by recommendation modules.
    /// </summary>
    public AppAnalysisResult Analyze()
    {
        return new AppAnalysisResult
        {
            AppType = "ASP.NET Core API",
            UsesDatabase = true,
            UsesBackgroundServices = true,
            UsesCaching = false,
            UsesFileUploads = true,
            ExpectedConcurrentUsers = 1000
        };
    }
}
