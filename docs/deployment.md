# Deployment

PoolCar runs on Azure Container Apps, with several named deployments side by side.

## Manual setup

No manual setup is required to run the application locally. Deploying to Azure needs this one-time setup by an Azure and GitHub administrator:

- [ ] Register the resource providers once for the subscription:

  ```sh
  for namespace in Microsoft.App Microsoft.DBforPostgreSQL Microsoft.ContainerRegistry \
    Microsoft.OperationalInsights Microsoft.Insights Microsoft.ManagedIdentity; do
    az provider register --namespace "$namespace"
  done
  ```

- [ ] Create the resource group `rg-poolcar` in UK South (`uksouth`).
- [ ] Create the deploy identity:
  - [ ] Entra app registration `poolcar-github` with a federated credential for the subject `repo:alanta@73225/poolcar@1416094794:environment:azure`.
  - [ ] On `rg-poolcar`, assign it Contributor and Role Based Access Control Administrator, with the condition that it may only assign the AcrPull role.
- [ ] Create the sign-in app:
  - [ ] Entra app registration `PoolCar` for accounts in any organizational directory (multi-tenant), with ID tokens enabled: under Authentication, select "ID tokens" for implicit grant and hybrid flows, or run `az ad app update --id <appId> --enable-id-token-issuance true`. The built-in Container Apps authentication uses the hybrid flow.
  - [ ] App role `FleetOperator` with allowed member type Users/Groups, assigned to the fleet operators.
  - [ ] A client secret.
- [ ] Create the GitHub environment `azure` with:
  - [ ] Variables `AZURE_CLIENT_ID` (of `poolcar-github`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and `EASYAUTH_CLIENT_ID` (of `PoolCar`). Optionally `AZURE_RESOURCE_GROUP`; it defaults to `rg-poolcar`.
  - [ ] Secret `EASYAUTH_CLIENT_SECRET` with the client secret of `PoolCar`.
- [ ] After the first deploy of each deployment, add its redirect URI `https://<web fqdn>/.auth/login/aad/callback` to `PoolCar` as a Web redirect URI. The workflow summary prints it; Entra ID doesn't allow wildcards.
- [ ] Protect `main`: require a pull request, require the status checks `Build`, `Test` and `End-to-end tests`, and block force pushes.
- [ ] Set the Actions log and artifact retention to 90 days.

## Deploying

The Deploy workflow deploys a named deployment to `rg-poolcar`. Pushes to `main` deploy `live`; run it manually for other deployments:

| Input | Description |
| --- | --- |
| `deployment` | Deployment name of 1-16 lowercase letters or digits, starting with a letter. Defaults to `live`. |
| `ref` | Branch, tag or commit to deploy. Defaults to `main`. |
| `reset-data` | Drop and reseed the deployment's database. |

A run deploys the shared infrastructure, builds the Web, Fleet, Reservations and DbSetup images and pushes them tagged with the commit SHA, runs the database setup job, and then deploys the apps. The job runs as `id-poolcar-dbadmin`, which owns the deployment's database: it creates the database, applies the Reservations migrations, seeds an empty database and gives the deployment's identity access to the data, but not to the schema. Reservations doesn't migrate or seed in Azure ([ADR 0005](adr/0005-apply-schema-changes-with-a-setup-job.md)). The run summary shows the web URL and redirect URI. Runs are queued one at a time, also across deployments, because they share the PostgreSQL server.

The apps run one replica on weekdays from 9:00 to 17:00 Dutch time and scale to zero otherwise, so the first request outside those hours waits for a cold start.

To add a deployment, run the workflow with a new name, then add its redirect URI to the `PoolCar` app registration.

`reset-data` drops the deployment's database before setup, which creates, migrates and seeds it again with the employees and sample bookings, relative to the day they're seeded.

To remove a deployment, for example `scratch`, delete its resources and remove its redirect URI from `PoolCar`:

```sh
az containerapp delete -g rg-poolcar -n ca-scratch-web --yes
az containerapp delete -g rg-poolcar -n ca-scratch-reservations --yes
az containerapp delete -g rg-poolcar -n ca-scratch-fleet --yes
az containerapp job delete -g rg-poolcar -n caj-scratch-dbsetup --yes
az identity delete -g rg-poolcar -n id-poolcar-scratch
az postgres flexible-server db delete -g rg-poolcar -s psql-poolcar -n poolcar_scratch --yes
```

| Scope | Resources |
| --- | --- |
| Shared | Container registry `crpoolcar`, Container Apps environment `cae-poolcar`, PostgreSQL flexible server `psql-poolcar`, Log Analytics `log-poolcar`, Application Insights `appi-poolcar`, identities `id-poolcar-pull` (image pulls) and `id-poolcar-dbadmin` (database administrator). |
| Per deployment | Container apps `ca-<name>-web`, `ca-<name>-fleet` and `ca-<name>-reservations`, job `caj-<name>-dbsetup`, identity `id-poolcar-<name>` and database `poolcar_<name>`. |

Only Web is reachable from the internet; it signs users in with the `PoolCar` app registration.
