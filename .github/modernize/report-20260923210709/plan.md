# Azure modernization plan

## Assessment reference
- Report ID: `report-20260923210709`
- Project: `ProgrammePulse`
- Language: `.NET` / C#
- Scope: Selected categories from the cloud-readiness assessment, filtered to this workspace only.

## Executive summary
The current application is a .NET 10 Umbraco site that has multiple cloud-readiness issues rooted in local file I/O, synchronous API usage, local configuration, hardcoded secrets, unmanaged certificates, Windows identity coupling, local caching, local system access, and runtime telemetry that is not aligned to Azure-native observability. This plan focuses on a staged move to Azure-managed services while preserving the existing business behavior and minimizing re-platforming risk.

The plan prioritizes:
1. Removing hardcoded configuration and local secrets from the application boundary.
2. Moving persistent local resources to Azure-managed storage and cache services.
3. Replacing legacy identity, certificate, and telemetry patterns with managed Azure services.
4. Modernizing HTTP and runtime operational patterns to support cloud hosting and Azure-native diagnostics.

## Selected assessment categories
- File System Management
- Performance Optimization
- Configuration Management
- HTTP Communication
- Local Credential
- Certificate Management
- Windows Ad
- Local File
- Local Cache
- Serilog
- Local System Dependencies

## Planned workstreams

### 1. Cloud configuration and secret hardening
- Replace hardcoded URLs and local application config with Azure App Configuration and environment-based configuration.
- Move secrets and TLS material to Azure Key Vault with managed identity-based access.
- Remove plaintext credential patterns and Windows AD assumptions in favor of Microsoft Entra ID.

### 2. Azure storage and platform isolation
- Move static content and file-backed workloads from local disk to Azure Blob Storage or Azure Files, based on access pattern.
- Replace local in-memory or local-disk caching with Azure Cache for Redis.
- Eliminate local file system and OS environment dependencies that do not scale in cloud hosting.

### 3. API and application runtime modernization
- Replace synchronous HTTP and local network access patterns with asynchronous and Azure-optimized communication patterns.
- Modernize HTTP clients to support external dependency resilience, retries, timeout configuration, and managed connectivity.
- Shift logging from Serilog-only patterns to OpenTelemetry and Azure Monitor / Application Insights.

### 4. Identity and security modernization
- Replace Windows AD-based authentication with Microsoft Entra ID.
- Migrate certificates to Azure Key Vault and ensure certificate retrieval is managed through secure Azure identity integration.
- Enforce managed identity and remove embedded secret material from code, config, and deployment artifacts.

## Task plan

1. File System Management Migration
   - KB ID: `file-system-management-prompt`
   - Goal: Replace local or network file I/O with Azure-appropriate storage and cloud-safe file access patterns.

2. Performance Optimization for Cloud
   - KB ID: `performance-optimization-prompt`
   - Goal: Remove synchronous API usage patterns that block throughput and cloud scale-out.

3. Configuration Management Modernization
   - KB ID: `configuration-management-prompt`
   - Goal: Remove hardcoded configuration and local app configuration with centralized Azure configuration management.

4. Externalize appsettings to Azure App Configuration (SDK-based)
   - KB ID: `local-appsettings-to-azure-app-configuration`
   - Goal: Use dynamic refresh and Azure App Configuration SDK integration for runtime configuration.

5. Externalize appsettings to Azure App Configuration (deployment-only)
   - KB ID: `local-appsettings-to-azure-app-configuration-via-deployment`
   - Goal: Use deployment-time injection where an SDK integration is not required.

6. HTTP Communication Modernization
   - KB ID: `http-communication-prompt`
   - Goal: Replace direct local/legacy HTTP patterns with cloud-safe client patterns and added resilience.

7. Migrate from plaintext credentials to secured credentials protected by Managed Identity and Azure Key Vault
   - KB ID: `plaintext-credential-to-azure-keyvault`
   - Goal: Eliminate embedded secrets and move credentials to Azure Key Vault with managed identity.

8. Migrate certificate to Azure Key Vault
   - KB ID: `certificate-to-azure-key-vault`
   - Goal: Move managed certificate handling to Azure Key Vault and remove local certificate dependency.

9. Migrate from Windows AD to Microsoft Entra ID
   - KB ID: `windows-ad-to-azure-ad`
   - Goal: Replace enterprise authentication dependency on Windows AD with modern Azure identity.

10. Migrate from local file system to Azure Blob Storage
    - KB ID: `local-file-to-azure-blob-storage`
    - Goal: Move static content and object-like files into Azure Blob Storage.

11. Migrate from local file system to Azure File Storage
    - KB ID: `local-file-to-azure-file-storage`
    - Goal: Support shared file and SMB-style workloads with Azure Files when file semantics are required.

12. Migrate from local cache to Azure Cache for Redis
    - KB ID: `local-cache-to-azure-redis-cache`
    - Goal: Move local cache state to a managed and scalable Redis service.

13. Migrate from Serilog to OpenTelemetry on Application Insights
    - KB ID: `serilog-to-opentelemetry`
    - Goal: Modernize observability and align logs, traces, and metrics with Azure Monitor and Application Insights.

14. Local System Dependencies Migration
    - KB ID: `local-system-dependencies-prompt`
    - Goal: Remove OS-level assumptions and local runtime dependencies that are incompatible with Azure hosting.

15. Security remediation and hardening
   - Goal: Validate the migration against the full cloud-readiness recommendations and apply the Azure security baseline, including secret rotation, managed identity, and secure configuration review.

## Dependencies and sequencing
The migration should proceed in the following order:
1. Configuration, secrets, certificate, and identity modernization
2. Storage and cache migration
3. Local system dependency removal and platform adaptation
4. Performance and HTTP communication optimization
5. Telemetry and observability modernization
6. Final security validation and Azure hardening

## Risk and mitigation
- Risk: Breaking local file assumptions in the application runtime.
  - Mitigation: Map each file usage to the correct Azure storage abstraction before migration.
- Risk: Secret rotation and credential drift during migration.
  - Mitigation: Introduce managed identity and Key Vault first, then remove plaintext values in controlled stages.
- Risk: Authentication changes affecting user sign-in and enterprise access.
  - Mitigation: Validate Entra integration in staging and keep role mapping deterministic.
- Risk: Performance regressions from synchronous HTTP and local cache removal.
  - Mitigation: Add async patterns and benchmark the revised dependency paths after migration.

## Rollout recommendation
Roll out in two phases:
- Phase 1: configuration, identity, and secret management modernization
- Phase 2: storage, cache, observability, and application runtime/cloud-specific refinements

This sequencing reduces operational risk while preserving the app’s current behaviour during the initial Azure adoption steps.
