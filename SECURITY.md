# Security

Do not put suspected vulnerabilities, credentials, customer data or exploit
details in public issues. Use this repository's GitHub private vulnerability
reporting feature if enabled, or the maintainer's agreed private support channel.
Include the affected revision, reproduction steps, impact and redacted evidence.
Never test against customer tenants without their authorization.

The [security assurance register](docs/security-assurance.md) maps controls to
code, tests and outstanding deployment evidence. It is the starting point for
customer reviews. Passing CI is not a penetration test, certification or proof
that a particular deployment is secure.

Before release, require successful build/test, dependency audit and secret scan
checks on the reviewed commit. Re-run the evidence after dependency or security
changes. The production host, secret manager and monitoring platform are not yet
selected; the deployment gates in the register must be closed before sign-off.
