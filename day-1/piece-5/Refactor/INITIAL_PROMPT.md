Generate a deliberately bad OrderController.cs for an ASP.NET Core Web API project.

Requirements:
- Around 300 lines long
- One giant POST /api/orders action
- Mix business logic, validation, EF Core database access, calculations, and HTTP response handling all inside the controller action
- Use synchronous EF Core calls inside an async action
- Return object instead of typed responses
- Include at least four empty catch { } blocks swallowing exceptions
- Include duplicated logic and magic numbers
- No DTOs
- Direct DbContext usage inside controller
- Include subtle bugs:
  - one off-by-one bug
  - one possible null reference bug
- No separation of concerns
- No tests
- Make it realistic legacy code written by a rushed developer two years ago

Also generate minimal supporting models/entities if needed.