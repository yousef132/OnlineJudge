# Online 

## Overview

This project provides a simple API endpoint that accepts a request from the client containing source code, the programming language, and test cases. The system executes the code in an isolated Docker container and returns the result.

### Request Format

The client sends a JSON object with the following structure:

```json
{
    "code": "string",
    "language": "string",
    "testcases": "string"
}
```

## How It Works
1- The system creates a file with the appropriate extension based on the provided programming language.

2- A Docker container is spun up, mounting the code, test cases, and output files.

3- The code runs inside the container in an isolated environment, ensuring security and containment.

4- the system runs the test cases against the code.

5- The output is retrieved from the container and returned as the API response.

## Benefits
* Isolation: The use of Docker ensures that the code runs in an isolated environment, minimizing security risks.
* Language Support: The system is extendable to support multiple programming languages by using different Docker images.
* Automated Execution: The entire process from code execution to output retrieval is automated, providing quick feedback.
