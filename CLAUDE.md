# Code Style

## General

- Follow Visual Studio (Microsoft) default formatting conventions for C#
- Use 4-space indentation (no tabs)
- Opening braces on the same line for lambdas/expressions, new line for methods/classes/properties (Allman style)
- One blank line between members; two blank lines between top-level type declarations
- Line endings: CRLF (`\r\n`) — matches Visual Studio default on Windows

## Naming

- `PascalCase` for types, methods, properties, events, and public fields
- `camelCase` for local variables and parameters
- `_camelCase` (underscore prefix) for private instance fields
- `s_camelCase` for private static fields
- Interfaces prefixed with `I` (e.g. `IRepository`)
- Do not use Hungarian notation

## Parameter and Argument Lists

When a parameter or argument list is too long to fit on one line, place each item on its own indented line with a trailing comma, and close on a new line:

```csharp
// Parameters
public void SomeMethod
(
    int alpha,
    string beta,
    CancellationToken cancellationToken,
)
{
}

// Arguments
SomeMethod
(
    alpha,
    beta,
    cancellationToken,
);
```

Do not mix inline and broken-out styles within the same call/declaration.

## XML Documentation Comments

All public types and members must have XML doc comments. Use the following structure:

```csharp
/// <summary>
/// Brief description of what this does.
/// </summary>
/// <param name="paramName">Description of the parameter.</param>
/// <returns>Description of the return value.</returns>
/// <exception cref="InvalidOperationException">When this is thrown.</exception>
/// <remarks>
/// Optional additional detail.
/// </remarks>
```

- The `<summary>` tag is always required for public members
- ONLY add `<param>` for parameters of which the usage is unclear (hardly ever)
- Add `<returns>` when the return type is non-void and non-obvious
- Add `<exception>` for every documented exception
- Use full sentences ending with a period inside tags

## Encapsulation

- All instance fields must be `private readonly` — expose state only through properties or methods
- Prefer `private` over `internal` unless cross-assembly access is explicitly needed
- Mark classes `sealed` by default; only leave unsealed when inheritance is intentional
- Prefer read-only fields (`readonly`) and init-only properties (`init`) wherever mutation is not required
- Avoid `public` setters on properties unless the type is a plain DTO; prefer constructor injection or builder patterns

## Access Modifier Order

Declare members in this order within a type:

1. `private` / `private static` fields
2. Constructors
3. `public` properties
4. `public` methods
5. `private` / `protected` methods

## Miscellaneous

- Use `var` when the type is obvious from the right-hand side; use the explicit type otherwise
- Prefer expression-bodied members for single-line getters and trivial methods
- Use file-scoped namespace declarations (`namespace Foo;`)
- No `#region` blocks
- No region indicating comments
- No trailing whitespace
- Files end with a single newline
