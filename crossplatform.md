# Cross Platform Approach

The key to code sharing cross-platform with dotnet is seperating out the concerns of your application is to develop a set of domainAPIs. In the C# world, this is abstraction is best implemented with the use of interfaces.

<diagram rxns domain api>

Traits & Patterns

* Program to interfaces | On the ground that means your aim should always be to implement your domainAPI in `.net standard`. That shouldbe 90% of your app. Then on each platform, 10% will be strategically integrated to utilise platform specific features via `.netcoreapp`, `Xamarin app`, `Full framework` or similiar tech.

<Example domain API>

* Use `DI` | Use inversion of control / a DI container to compose the services of your App with and augment them with platform-specific features in a seemless fashion

<liecycle create example>


