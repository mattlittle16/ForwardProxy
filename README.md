# Forward Proxy

This project implements a simple forward proxy server using ASP.NET Core.

## Description

The primary purpose of this application is to receive incoming HTTP requests and forward them to a destination URL specified in the `x-forward-url` request header. It then returns the response from the destination server back to the original client.

Key functionalities include:
- Reading the target URL from the `x-forward-url` header.
- Forwarding the request method, headers (with some exclusions like `Host`, `Content-Length`), cookies, and body to the target URL.
- Returning the status code and response data from the target server.
- Option to ignore SSL certificate errors when forwarding requests (configured in `Program.cs`).
- Request body buffering to allow re-reading of the input stream.

## How it Works

1.  An HTTP request is sent to this proxy application.
2.  The `ForwardController` receives the request.
3.  It checks for the presence of the `x-forward-url` header. If missing, it returns a `400 Bad Request`.
4.  The `ForwardService` is invoked to handle the forwarding logic:
    *   It creates a new `HttpRequestMessage` with the method and target URL from the incoming request.
    *   It copies relevant headers and cookies from the original request to the new request. Certain headers like `Host`, `x-forward-url`, `Content-Length`, `Transfer-Encoding`, and `Connection` are skipped.
    *   If the original request has a body, it's read and set as the content of the new request.
    *   An `HttpClient` (configured to ignore SSL errors) sends the new request to the target URL.
5.  The response (status code and data) from the target server is packaged into a `ForwardModel`.
6.  The `ForwardController` returns this response to the original client.

## How to Run

### Using Docker

The project includes a `Dockerfile` and `docker-compose.yml` for easy containerization and deployment.

1.  Make sure you have Docker installed and running.
2.  Navigate to the root directory of the project in your terminal.
3.  Run the command:
    ```bash
    docker-compose up --build
    ```
    This will build the Docker image and start the proxy service. By default, it will be accessible on port 80 of your host machine.

### Running Locally (Development)

1.  Ensure you have the .NET SDK installed.
2.  Navigate to the `src` directory.
3.  Run the command:
    ```bash
    dotnet run
    ```
    The application will start, and the listening URLs will be displayed in the console (typically `http://localhost:5000` and `https://localhost:5001` or as configured in `Properties/launchSettings.json`).

## Key Components

*   `ForwardController.cs`: The API controller that handles incoming requests.
*   `ForwardService.cs`: The service class responsible for the core proxy logic (constructing and sending the outgoing request).
*   `ForwardModel.cs`: A record struct to hold the status code and response data from the forwarded request.
*   `Program.cs`: Configures the ASP.NET Core application, including services, HTTP client (with SSL ignore), and middleware.
*   `Dockerfile` & `docker-compose.yml`: For building and running the application in a Docker container.

## License

This project is licensed under the MIT License.