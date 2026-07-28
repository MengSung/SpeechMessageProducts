# Sunnyvalechback 9.1 ID3242 re-analysis after new server evidence

## Role

Act as a Dynamics 365 on-premises IFD, ADFS, WS-Trust, WCF and authentication
debugger. Re-evaluate the earlier root-cause hypothesis using the new evidence.
Do not expose passwords or recommend switching the product to internal
AD/Windows authentication.

## Non-negotiable deployment constraint

- Dynamics 365 CE 9.1 is on-premises IFD.
- The application and future cloud Gateway are internet-facing.
- Runtime traffic must continue to use the public HTTPS IFD endpoint.
- Never replace this with an internal AD/Windows-authentication product path.
- Internal IPs are allowed only for server administration and diagnostics.

## User-visible symptom

ChurchReport member login calls the legacy SOAP connection pool before querying
the member contact. The first CRM operation fails with:

```text
ID3242: 無法驗證或授權此安全性權杖。
```

The website member account/password has not yet been evaluated when this occurs.

## Corrected identity evidence

The Dynamics 365 9.1 user page for the signed-in system administrator visibly
shows the exact user name:

```text
SPEECHMESSAGE\Administrator
```

The browser is successfully signed in to:

```text
https://sunnyvalechback.speechmessage.com.tw/main.aspx
```

Therefore the earlier inference that `SPEECHMESSAGE\Administrator` is invalid
because `CrmConnection:Domain` says `DYNAMICS-365` is not supported. The code
does not consume the `Domain` field, and the CRM UI is stronger evidence that
the qualified username is valid in this organization.

The configured username is currently kept as `SPEECHMESSAGE\Administrator`.

## Verified topology

- D365 internal management host: `192.168.50.20` (80/443 and WinRM 5985 open)
- DC/ADFS internal management host: `192.168.5.100` (not directly reachable
  from the development workstation)
- Public D365 IFD host: `sunnyvalechback.speechmessage.com.tw`
- Public ADFS host: `adfsdev91.speechmessage.com.tw`
- Both public names resolve to `220.134.52.83`
- Organization.svc WSDL is HTTP 200 and declares Federation authentication
- Its issuer MEX is `https://adfsdev91.speechmessage.com.tw/adfs/services/trust/mex`
- The ADFS MEX publishes WS-Trust 1.3 and 2005 `usernamemixed` endpoints
- `/api/data/v9.1/` exists and returns the expected unauthenticated HTTP 401

## Credential-management probe

One WinRM authentication attempt to the D365 internal host using the configured
CRM username/password returned `Access is denied`. This is NOT conclusive proof
that the password is wrong because WinRM authorization/local policy can reject
an otherwise valid CRM/AD user. No repeated attempts should be made to avoid
account lockout.

## Legacy client implementation

```text
ChurchReport -> CrmConnectionPool -> OnPremiseClient
  -> reads Organization.svc WSDL
  -> discovers adfsdev91 MEX
  -> selects WS-Trust 1.3 UsernameToken policy
  -> constructs ServerEntropyWS2007HttpBinding
  -> WSTrustTokenParameters.CreateWS2007FederationTokenParameters
  -> WSFederationHttpBinding to public Organization.svc
```

The client sends the configured username string unchanged. It sets
`EstablishSecurityContext=false`, server entropy, and the SDK client header from
the loaded Microsoft.Xrm.Sdk assembly version. The same borrowed client worked
against the older jesus CE 8.2 IFD environment.

## Questions

1. Given the corrected username evidence, rank the plausible causes of ID3242.
   Consider stale/incorrect configured password, ADFS event evidence, CRM
   relying-party identifiers/audience, token-signing or encryption certificate
   trust/rollover, claims rules, time skew, disabled WS-Trust endpoint, and
   custom WCF binding incompatibility.
2. Explain how to distinguish whether ADFS failed to issue the token versus CRM
   rejected a token that ADFS did issue.
3. Specify the smallest safe application-side diagnostic that captures the
   exception chain/stage without logging username/password/token.
4. Specify exact ADFS and D365 server-side logs/configuration to inspect and the
   decision tree for each result.
5. Should `CrmConnection:Username` remain `SPEECHMESSAGE\Administrator`, use a
   UPN, or change only if ADFS/AD evidence proves another canonical form?
6. Identify any known weakness in the custom `OnPremiseClient` WCF/WS-Trust
   construction that can work on CE 8.2 but fail on CE 9.1.

## Expected output

- Corrected root-cause ranking with confidence levels
- Evidence required before changing Username or password
- Minimal diagnostic implementation proposal
- ADFS/D365 server inspection commands or event IDs
- Safe repair decision tree
- Critical/Warning/Info findings

