# Rxn.Create

Is a command-line interface into your Reactions that uses the same API as in-code reactions.

Usage:
`rxn.create {feature}`

Feature | Options | Usage
-|-|-
`SpareReactor` | `{name} {version} {binary} {applocation} {isLocal} {appStatusUrl}` | Simplify Docker or other scaling integrations. Adds a scaling resource to your AppStatus portal that will be used by your `IScaleoutPlan`
`FromAppUpdate` | `{name} {version} {binary} {applocation} {isLocal} {appStatusUrl}` | Installs a reaction app using an App update as its source
`NewAppUpdate` | `{name} {version} {applocation} {isLocal} {appStatusUrl}` |Creates an app update that can be later downloaded or used for a `IScaleoutPlan`
`SpareReactor` | `{appStatusUrl}` | Simplify Docker or other scaling integrations. Adds a scaling resource to your AppStatus portal that will be used by your `IScaleoutPlan`
`ReactorFor` | `{appStatusUrl}` | Turn existing apps into Reactions that report to the AppStatus portal and can cluster with other reactions in basic ways