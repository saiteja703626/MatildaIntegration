
// Find the HTML elements we need
const chatForm = document.getElementById("chat-form");
const userInput = document.getElementById("user-input");
const chatMessages = document.getElementById("chat-messages");

// Listen for the user submitting the form
chatForm.addEventListener("submit", async function (event) {

    // Prevent the browser from refreshing the page
    event.preventDefault();

    // Get the question typed by the user
    const message = userInput.value.trim();

    // Don't send an empty message
    if (message === "") {
        return;
    }

    // Display the user's question
    addMessage(message, "user");

    // Clear the input box
    userInput.value = "";

    // Display a temporary message
    const loadingMessage = addMessage("Matilda is thinking...");

    try {

        // Send the question to our C# backend
        const response = await fetch("/matilda/ask", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                message: message
            })
        });

        // Convert the response into JavaScript data
        const data = await response.json();

        // Check whether the request was successful
        if (!response.ok) {
            throw new Error(data.error || "Something went wrong.");
        }

        // Display Matilda's answer
        loadingMessage.textContent = data.answer;

    } catch (error) {

        // Display an error if something goes wrong
        loadingMessage.textContent = "Error: " + error.message;

    }

});

// Function to add a message to the chat window
function addMessage(message, sender) {

    const messageElement = document.createElement("div");

    messageElement.classList.add("message");

    if (sender === "user") {
        messageElement.classList.add("user-message");
    } else {
        messageElement.classList.add("assistant-message");
    }

    messageElement.textContent = message;

    chatMessages.appendChild(messageElement);

    // Scroll to the latest message
    chatMessages.scrollTop = chatMessages.scrollHeight;

    return messageElement;
}
