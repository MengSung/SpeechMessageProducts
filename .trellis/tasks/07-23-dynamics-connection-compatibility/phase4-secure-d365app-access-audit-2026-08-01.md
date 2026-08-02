# Phase 4 D365APP01 secure access audit — 2026-08-01

## Purpose

Attempt to take over the approved D365APP01 administration work directly from
the current controller without asking an operator to type commands, while
preserving the deployment security boundary.  This audit never creates a
credential, persists a session, changes DNS, changes WinRM, or changes a D365
setting.

## Current controller boundary

| Check | Bounded result |
| --- | --- |
| Current Windows identity | Local `LENOVO-LEGION` administrator identity; not joined to the domain |
| Kerberos prerequisite | No domain Kerberos identity or target-host DNS name is available in this logon session |
| D365APP01 WinRM transport | The existing internal listener port is reachable |
| Existing PowerShell remoting owner | None; final owned `PSSession` count is zero |
| Kerberos-only remote session | Rejected before a D365 command can execute |
| Existing VM console | A D365-9.1 VMConnect display is open and already logged in, but controlled input cannot be forwarded into the guest |

The specific private address, Kerberos/WSMan response body, ticket details,
credential material, and VM console content are intentionally not recorded.

## Security decision

The reachable listener is not an authorization path.  The controller cannot
turn it into one by using an IP address, a local account, or a fallback
protocol.  The following were deliberately not attempted:

- NTLM, Basic, CredSSP, password prompting, `PSCredential`, Credential Manager,
  `TrustedHosts`, or unencrypted WinRM;
- DNS/hosts-file changes to synthesize a Kerberos target;
- Hyper-V privilege escalation or PowerShell Direct with a guest credential;
- automating the guest PowerShell ISE or invoking a terminal through VMConnect;
- SQL, Registry, IIS, DNS, AD FS, or CRM configuration as a substitute for
  authenticated Deployment PowerShell access.

The existing VM console provides visual evidence of the D365APP01 Deployment
shell only.  It does not confer a controllable administrative transport and
cannot safely be used to inject terminal commands.

## Consequence for Phase 4

The D365APP01 Claims/IFD correction and its live `WhoAmI` proof remain pending
an already-approved domain Kerberos/Negotiate administration context that is
controllable from the current session.  This is independent from the LocalDB
cross-process capacity/fencing proof, which continues locally.  Phase 5 and
Phase 6 remain locked.

## Automated next opportunity

If an already approved Kerberos/Negotiate administrative session becomes
available on this controller, the next action is one fresh official
`Get-CrmSetting -SettingType IfdSettings` read in that session, followed only
by the bounded IFD correction/verification procedure identified from the
current CRMWeb failure.  The agent must again remove its owned `PSSession` in a
`finally` block and never retain remote objects, identities, or credentials.
