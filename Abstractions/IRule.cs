namespace DeploymentGuardian.Abstractions;

public interface IRule<T>
{
    /// <summary>
    /// Evaluates a context and returns any alerts triggered by this rule.
    /// </summary>
    List<Models.Alert> Evaluate(T context);
}
