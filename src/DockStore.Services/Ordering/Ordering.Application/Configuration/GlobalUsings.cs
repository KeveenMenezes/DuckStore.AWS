global using System.Runtime.CompilerServices;
global using BuildingBlocks.CQRS;
global using BuildingBlocks.Messaging.Events;
global using BuildingBlocks.Pagination;
global using FluentValidation;
global using MassTransit;
global using MediatR;
global using Microsoft.Extensions.Logging;
global using Ordering.Application.Dtos;
global using Ordering.Application.Extensions;
global using Ordering.Application.Orders.Commands.CreateOrder;
global using Ordering.Domain.Abstractions.Repositories;
global using Ordering.Domain.Enums;
global using Ordering.Domain.Events;
global using Ordering.Domain.Exceptions;
global using Ordering.Domain.Models;
global using Ordering.Domain.ValueObjects;

