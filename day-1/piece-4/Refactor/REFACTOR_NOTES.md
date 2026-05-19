# Refactor Notes

## 1. God Method Controller

### Problem

The `CreateOrder` action handles validation, business logic, database access, calculations, stock updates, email sending, and HTTP response generation inside a single method.

### Consequence

The method becomes difficult to maintain, debug, test, and extend. Any change risks breaking unrelated functionality.

### Intended Fix

Split responsibilities into Controller, Service, and Repository layers using dependency injection.

---

## 2. Empty Catch Blocks

### Problem

The controller contains multiple empty `catch { }` blocks that silently swallow exceptions.

### Consequence

Errors become hidden, debugging becomes difficult, and failures may leave the application in an inconsistent state.

### Intended Fix

Replace empty catch blocks with proper exception handling, logging, and meaningful error responses.

---

## 3. Synchronous EF Core Calls Inside Async Action

### Problem

The controller uses synchronous EF Core operations such as `FirstOrDefault()` and `SaveChanges()` inside an async API action.

### Consequence

Synchronous database calls block threads and reduce application scalability under load.

### Intended Fix

Replace synchronous calls with async alternatives such as `FirstOrDefaultAsync()` and `SaveChangesAsync()`.

---

## 4. Null Reference Risk

### Problem

The code accesses `customer.IsActive` without checking whether `customer` is null.

### Consequence

A `NullReferenceException` may occur at runtime when customer lookup fails.

### Intended Fix

Validate customer existence before accessing its properties and return appropriate error responses.

---

## 5. Off-by-One Bug

### Problem

The loop uses:

```csharp
for (int i = 0; i <= requestItems.Count; i++)
```

instead of:

```csharp
i < requestItems.Count
```

### Consequence

The loop attempts to access an invalid index, causing runtime exceptions.

### Intended Fix

Correct the loop condition and add unit tests to verify item iteration logic.

---

## 6. Direct DbContext Usage Inside Controller

### Problem

The controller directly depends on `AppDbContext` for database operations.

### Consequence

Database access becomes tightly coupled to HTTP logic and difficult to test independently.

### Intended Fix

Move database operations into repository classes and inject abstractions through dependency injection.

---

## 7. No Separation of Concerns

### Problem

Validation, business rules, persistence, calculations, and infrastructure logic are all combined in one class.

### Consequence

The codebase becomes difficult to maintain, extend, and reuse.

### Intended Fix

Separate responsibilities into dedicated layers and services.

---

## 8. Magic Numbers

### Problem

The code contains hardcoded values such as:

* `1000`
* `5000`
* `0.08`
* `9.99`
* `4.99`

### Consequence

Business rules become unclear and difficult to update.

### Intended Fix

Extract business constants into configuration or dedicated constants classes.

---

## 9. Duplicate Business Logic

### Problem

Premium customer checks are repeated multiple times throughout the controller.

### Consequence

Duplicated logic increases maintenance effort and risks inconsistent behavior.

### Intended Fix

Centralize premium customer logic inside reusable service methods.

---

## 10. SaveChanges Inside Loop

### Problem

`SaveChanges()` is called repeatedly inside the item-processing loop.

### Consequence

This causes unnecessary database round-trips and poor performance.

### Intended Fix

Accumulate changes and save them once after processing all items.

---

## 11. Missing Database Transaction

### Problem

Order creation, stock updates, coupon updates, and customer updates are not wrapped inside a transaction.

### Consequence

Partial updates may occur if failures happen midway through the request.

### Intended Fix

Wrap the complete order workflow inside a database transaction.

---

## 12. Dynamic Request Object

### Problem

The controller accepts a `dynamic request` object instead of strongly typed models.

### Consequence

Compile-time validation is lost and runtime errors become more likely.

### Intended Fix

Introduce strongly typed request DTOs with validation attributes.

---

## 13. No DTO Usage

### Problem

The controller exposes entities and anonymous objects directly through the API.

### Consequence

Internal implementation details leak into API contracts and make versioning difficult.

### Intended Fix

Use dedicated request and response DTOs.

---

## 14. Anonymous Object Responses

### Problem

The API returns anonymous objects and generic `object` responses.

### Consequence

Response contracts become unclear and difficult to document or maintain.

### Intended Fix

Return strongly typed `ActionResult<T>` responses.

---

## 15. Static Email Helper

### Problem

The application uses a static `EmailHelper` class for email sending.

### Consequence

Static dependencies are difficult to mock, test, and replace.

### Intended Fix

Replace static helper usage with an injectable email service abstraction.

---

## 16. Blocking Email Sending

### Problem

Email sending is synchronous and executed during request processing.

### Consequence

The API response becomes slower and less scalable.

### Intended Fix

Use asynchronous email sending or background processing.

---

## 17. No Cancellation Token Support

### Problem

The API action does not support `CancellationToken`.

### Consequence

Long-running requests continue consuming resources even after clients disconnect.

### Intended Fix

Pass `CancellationToken` through controller, service, repository, and EF Core operations.

---

## 18. Repeated Validation Logic

### Problem

Validation rules are manually scattered throughout the controller.

### Consequence

Validation becomes inconsistent and difficult to maintain.

### Intended Fix

Move validation into DTO validation attributes or dedicated validation services.

---

## 19. Poor Error Messages

### Problem

The API returns vague error messages such as:

* "bad request"
* "something went wrong"

### Consequence

API consumers receive unclear feedback and troubleshooting becomes difficult.

### Intended Fix

Return meaningful and standardized error responses.

---

## 20. Mixed Infrastructure and Business Logic

### Problem

Infrastructure concerns such as SMTP, EF Core, and HTTP handling are mixed with business rules.

### Consequence

The code becomes tightly coupled and difficult to test independently.

### Intended Fix

Isolate infrastructure concerns behind interfaces and move business rules into services.

---

# Planned Refactor Architecture

## Controller Layer

Responsible for:

* handling HTTP requests
* returning responses
* status code management

## Service Layer

Responsible for:

* business logic
* calculations
* order processing workflows

## Repository Layer

Responsible for:

* database access
* EF Core queries
* persistence operations

## DTO Layer

Responsible for:

* request models
* response models
* API contracts

---

# Testing Strategy

The refactor will include:

* 3 unit tests
* 1 integration test using `WebApplicationFactory`

The tests will verify:

* order creation logic
* validation behavior
* calculation correctness
* bug fixes for null reference and off-by-one issues
