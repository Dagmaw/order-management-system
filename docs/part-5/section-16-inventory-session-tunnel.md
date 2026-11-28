# Section 16 - Private Inventory Swagger through Session Manager

This extension gives the developer browser access to Inventory without making the service public. The API, Inventory, RDS and existing network stay running. A small private EC2 helper is started only when needed.

## 1. Access path

Chrome → Windows loopback HTTPS port 443 → Docker relay → Session Manager encrypted channel → private helper → private Inventory ALB HTTPS → Java Inventory.

The helper has no public IP, SSH key, inbound security-group rules or database access. It uses the existing application subnet and NAT route. A dedicated operator role can start/stop only this helper and open only the fixed Inventory forwarding document. It cannot choose another host or open a shell.

Inventory Swagger and OpenAPI are enabled in the existing Inventory image. Forwarded-header handling preserves the HTTPS server URL behind the ALB. Stock-changing endpoints still require the appropriate service JWT scopes; opening Swagger grants no stock permissions.

## 2. Prepare and review infrastructure

1. Review `infra/aws/dev/inventory-tunnel.tf` and the two scoped IAM policy documents under `infra/iam/inventory-tunnel-terraform-*.json`.
2. Run `./scripts/initialize-inventory-tunnel-permissions.ps1` using the project administrator AWS profile to install the reviewed Terraform permissions. These permit this helper, its instance profile, and the fixed forwarding document. Existing Inventory role/network management scopes are reused.
3. Merge the reviewed changes after CI passes.
4. Dispatch **Terraform AWS Dev** on `main` with apply enabled.
5. Review the saved plan. Expect the helper, its IAM resources, forwarding document and security-group rules, plus a new Inventory task revision and service rollout. No database replacement, new NAT, public IP or API image change is expected.
6. Approve the existing `terraform-dev` environment gate. The workflow applies the exact reviewed plan and runs the normal deployment checks.
7. Record the non-secret `inventory_swagger_tunnel.instance_id` output. The helper starts running when Terraform creates it; stop it after verification.

## 3. Start the tunnel from Windows

Prerequisites: Docker Desktop using Linux containers, Chrome, the project AWS credentials profile, and available local port 443. Neither the native AWS CLI nor the native Session Manager plugin is required.

From the repository root:

```powershell
./scripts/Start-InventorySwaggerTunnel.ps1 -InstanceId i-REPLACE_WITH_OUTPUT
```

1. The script builds the local client. The AWS CLI base image is pinned; the plugin is downloaded from AWS and its signature is checked against the bundled AWS signing-key fingerprint.
2. The script assumes the dedicated tunnel operator role through the existing profile. Shared credentials are mounted read-only; they are not copied into the image or repository.
3. A stopped helper is started. The script waits for EC2 running and the SSM agent online.
4. Docker publishes the tunnel only on `127.0.0.1:443`.
5. The script checks the private OpenAPI URL using the correct hostname and normal TLS certificate validation.
6. An isolated Chrome profile opens the private Swagger URL. Its DNS override maps only the Inventory hostname to loopback. No system hosts-file edit or certificate bypass is used.

Browser URL:

```text
https://inventory.dev.internal.kaddigitalsolutions.com/swagger-ui/index.html
```

Use the Chrome window opened by the script. An ordinary browser window still cannot resolve the private hostname from the internet.

Optional: save the instance ID in ignored `.local-inventory-tunnel/settings.json`:

```json
{"instance_id":"i-REPLACE_WITH_OUTPUT"}
```

Then omit `-InstanceId` on future starts/stops. Use `-NoBrowser` to check the tunnel without opening Chrome. Use `-Profile` if the existing credentials profile has another name.

## 4. Verify access

1. Confirm the Swagger page loads without a certificate warning.
2. Confirm `/v3/api-docs` loads and the generated server URL uses HTTPS and the private Inventory hostname.
3. Expand `GET /api/inventory/{productId}`, choose an existing product, and execute a read-only lookup.
4. Confirm the result is 200 with the current stock. Do not change stock simply to test the tunnel.
5. Protected operations must still reject missing or insufficient tokens. The public .NET customer/admin token is separate from the Inventory service-token contract.

## 5. Stop the helper when finished

```powershell
./scripts/Stop-InventorySwaggerTunnel.ps1 -InstanceId i-REPLACE_WITH_OUTPUT
```

This stops the local tunnel and requests EC2 helper shutdown. It leaves the project API, Inventory, database, load balancers and NAT running. Close the isolated Chrome window. `-KeepHelperRunning` stops only the local tunnel and continues EC2 billing.

The helper's encrypted EBS volume remains while stopped. Terraform may report no change after a helper is stopped; restarting is an operator action, not an application deployment.

## 6. Cost and troubleshooting

The previously reviewed estimate is about $0.81/month for 20 running hours plus the persistent 8GB volume, or about $6.77/month if left running continuously, before minor traffic/tax. Existing NAT costs are separate. No additional NAT or public address is created. Recheck regional pricing when changing the design.

- **Private hostname fails in another browser:** use the isolated Chrome window created by the script.
- **Port 443 busy:** stop the conflicting local listener before starting. Do not publish the tunnel on all interfaces.
- **AssumeRole denied:** check the dedicated role trust and the source profile identity. Do not substitute the Terraform or administrator role into the tunnel container.
- **SSM offline:** check helper instance state, instance profile, agent service and existing outbound HTTPS/NAT route.
- **TLS verification fails:** check the Inventory ALB certificate and hostname; do not add `--insecure` or ignore browser warnings.
- **Swagger returns 404:** confirm the Inventory task revision has `API_DOCS_ENABLED=true`.
- **Swagger generates HTTP URLs:** confirm `SERVER_FORWARD_HEADERS_STRATEGY=framework` is present in the active task revision.
- **Session disconnects:** inspect `docker logs oms-inventory-swagger-tunnel`, then stop/restart the tunnel. Session credentials expire; a new start assumes the role again.

## Validation record

Implementation and live deployment evidence are recorded in the pull request. Infrastructure is not considered deployed until the approved workflow and the local HTTPS/browser checks pass.
