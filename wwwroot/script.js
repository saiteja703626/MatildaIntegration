
// Store the conversation history
const conversationHistory = [];

// Find the HTML elements we need
const chatForm = document.getElementById("chat-form");
const userInput = document.getElementById("user-input");
const chatMessages = document.getElementById("chat-messages");
const submitButton = chatForm.querySelector('button[type="submit"]');

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
    const loadingMessage = addMessage(
        "Matilda is thinking...", "assistant"
    );

    try {

        // Prepare the complete conversation and removed limit of 50 messages
        const messages = [
            ...conversationHistory.slice(-48),
            {
                role: "user",
                content: message
            }
        ];

        // Send the conversation to our C# backend
        const response = await fetch("/matilda/ask", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({
                messages: messages
            })
        });


        // Read the response as plain text first
        const responseText = await response.text();

        let data;

        try {
            // Convert the response text into JavaScript data
            data = JSON.parse(responseText);
        }
        catch {
            // Handle responses that are not valid JSON
            throw new Error("The server returned an invalid response. Please try again later.");
        }

        // Check whether the request was successful
        if (!response.ok) {
            throw new Error(data.error || "Something went wrong. Please try again.");
        }


        // Display Matilda's answer
        loadingMessage.textContent = data.answer;

        // Save the successful exchange in conversation history
        conversationHistory.push(
            {
                role: "user",
                content: message
            },
            {
                role: "assistant",
                content: data.answer
            }
        );

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
