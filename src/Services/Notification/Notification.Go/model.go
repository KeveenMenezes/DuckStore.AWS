package main

import "time"

// NotificationEvent represents a checkout notification to be stored in DynamoDB.
type NotificationEvent struct {
	ID            string  `json:"id" dynamodbav:"PK"`
	EventType     string  `json:"eventType" dynamodbav:"EventType"`
	UserName      string  `json:"userName" dynamodbav:"UserName"`
	CustomerID    string  `json:"customerId" dynamodbav:"CustomerId"`
	TotalPrice    float64 `json:"totalPrice" dynamodbav:"TotalPrice"`
	FirstName     string  `json:"firstName" dynamodbav:"FirstName"`
	LastName      string  `json:"lastName" dynamodbav:"LastName"`
	EmailAddress  string  `json:"emailAddress" dynamodbav:"EmailAddress"`
	AddressLine   string  `json:"addressLine" dynamodbav:"AddressLine"`
	Country       string  `json:"country" dynamodbav:"Country"`
	State         string  `json:"state" dynamodbav:"State"`
	ZipCode       string  `json:"zipCode" dynamodbav:"ZipCode"`
	CardName      string  `json:"cardName" dynamodbav:"CardName"`
	CardNumber    string  `json:"cardNumber" dynamodbav:"CardNumber"`
	Expiration    string  `json:"expiration" dynamodbav:"Expiration"`
	CVV           string  `json:"cvv" dynamodbav:"CVV"`
	PaymentMethod int     `json:"paymentMethod" dynamodbav:"PaymentMethod"`
	Status        string  `json:"status" dynamodbav:"Status"`
	ProcessedAt   string  `json:"processedAt" dynamodbav:"ProcessedAt"`
	OccurredOn    string  `json:"occurredOn" dynamodbav:"OccurredOn"`
}

// NewNotificationID generates a unique notification ID based on the current timestamp.
func NewNotificationID() string {
	return "notif-" + time.Now().UTC().Format("20060102T150405.000000000")
}
