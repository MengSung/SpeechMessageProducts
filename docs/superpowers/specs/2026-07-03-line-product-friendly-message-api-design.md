# LINE Product-Friendly Message API Design

## Goal

Provide a reusable LINE message construction API for ChurchReport and future ASP.NET Core products such as maintenance, membership, and invoice collection systems. Product code should be able to build common LINE messages without knowing every low-level LINE SDK constructor or JSON property.

## Current State

- `LineNotificationContent.TextMessage(...)`, `ImageMessage(...)`, and `FlexMessage(...)` already provide shared workflow content wrappers.
- `Line.Messaging` already contains SDK message objects for text, image, video, audio, location, sticker, template, Flex, imagemap, quick reply, and template actions.
- `Line.Messaging` does not currently contain send-message objects for `textV2` or `coupon`.
- `Line.Messaging` contains Coupon management API objects, but those are not the same as a sendable Coupon message object.

## Scope

This slice adds product-friendly message factories while keeping the boundary clean:

- Add reusable factories in `LineMessagingProcessor.Workflows`.
- Add SDK model support only where the current SDK lacks a sendable message object.
- Do not move ChurchReport CRM, payment, controller, database, or product workflow code into the LINE shared projects.
- Do not replace every ChurchReport LINE call site in this slice.
- Do not add all official Messaging API endpoints.

## Architecture

### `Line.Messaging`

Owns official LINE message objects and JSON shape. This layer should stay close to LINE's official message contract.

New SDK objects in this slice:

- `TextV2Message`: sendable message object with `type: "textV2"`.
- `CouponMessage`: sendable message object with `type: "coupon"`.

### `LineMessagingProcessor.Workflows`

Owns product-friendly factories for shared notification workflows. Future products call these factories instead of scattering `new TextMessage(...)`, `new TemplateMessage(...)`, or `new QuickReply(...)` across product code.

New shared API in this slice:

- `LineNotificationContent.TextMessageV2(...)`
- `LineNotificationContent.StickerMessage(...)`
- `LineNotificationContent.VideoMessage(...)`
- `LineNotificationContent.AudioMessage(...)`
- `LineNotificationContent.LocationMessage(...)`
- `LineNotificationContent.CouponMessage(...)`
- `LineNotificationContent.ConfirmTemplateMessage(...)`
- `LineNotificationContent.ButtonsTemplateMessage(...)`
- `LineNotificationContent.CarouselTemplateMessage(...)`
- `LineNotificationContent.ImageCarouselTemplateMessage(...)`
- `LineNotificationContent.ImagemapMessage(...)`
- `LineQuickReplyFactory`
- `LineTemplateActionFactory`
- `LineImagemapActionFactory`
- `LineCarouselColumnFactory`

## Contracts

- Factory methods return `LineNotificationContent`, not ChurchReport product types.
- Factory methods validate required values before any HTTP call.
- URL fields that LINE requires as HTTPS must be absolute HTTPS URLs.
- Quick reply helper rejects more than 13 items.
- Confirm template helper requires exactly 2 actions.
- Buttons template helper allows 1 to 4 actions.
- Carousel helper allows 1 to 10 columns.
- Carousel columns allow 1 to 3 actions.
- Imagemap helper allows 1 to 50 actions.
- Product code can still use `SdkMessagesList(...)` as an escape hatch for unsupported official message objects.

## Validation Strategy

The shared factories perform low-cost, deterministic validation:

- Required strings: reject null, empty, or whitespace.
- HTTPS URLs: reject null, empty, relative, non-HTTP, and non-HTTPS values.
- Template/action counts: reject missing or out-of-range collections.
- Numeric fields: audio duration must be positive; location coordinates must be within latitude and longitude ranges.

The factories do not validate remote file size, media codec, image dimensions, or whether a URL is publicly downloadable. Those are provider-side constraints enforced by LINE.

## Testing Strategy

- Unit tests serialize messages through `LineNotificationWorkflow` and assert the outbound LINE push payload.
- Tests cover each product-friendly wrapper's JSON shape.
- Tests cover validation failures before HTTP calls.
- Tests cover new SDK message objects directly enough to prove `type` values are serialized as official LINE strings.

## Non-Goals

- No new ChurchReport UI or controller behavior.
- No migration of all legacy `PushUtility`, `ReplyUtility`, or `LineUtilityClass` call sites in this slice.
- No all-official-API completion claim.
- No direct dependency from shared LINE projects back to ChurchReport, CRM, payment, or ASP.NET MVC.

