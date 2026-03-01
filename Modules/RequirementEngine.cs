using DeploymentGuardian.Abstractions;
using DeploymentGuardian.Models;

namespace DeploymentGuardian.Modules;

public class RequirementEngine
{
    private readonly IEnumerable<IRule<ServerContext>> _rules;

    /// <summary>
    /// Creates a rule engine with all rules to execute per evaluation cycle.
    /// </summary>
    public RequirementEngine(IEnumerable<IRule<ServerContext>> rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    /// <summary>
    /// Applies all registered rules to the given server context.
    /// </summary>
    public List<Alert> Evaluate(ServerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var alerts = new List<Alert>();

        foreach (var rule in _rules)
        {
            alerts.AddRange(rule.Evaluate(context));
        }

        return alerts;
    }
}
