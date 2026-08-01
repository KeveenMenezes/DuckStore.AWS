using Challenges.Function.Modules.Questions.Domain.Entities;
using Challenges.Function.Modules.Questions.Domain.Enums;
using Challenges.Function.Modules.Questions.Domain.ValueObjects;

namespace Challenges.DevelopmentDataSeeder;

// Ports the question bank out of the SPA bundle (ADR-0045 §10): every entry here is a 1:1 copy of
// src/WebApps/Shopping.Web.SPA.React/features/challenges/data/challenges.data.ts, which is deleted
// once the SPA reads through GraphQL instead (CH-10, not this task).
public static class ChallengesInitialData
{
    public static IEnumerable<Question> Questions =>
    [
        Question.Create(
            QuestionId.Of("py-001"),
            "Reversed List",
            "This Python code tries to reverse a list, but it has a bug. Find the error!",
            """
            def reverse_list(lst):
                reversed_lst = []
                for i in range(len(lst)):
                    reversed_lst.append(lst[i])
                return reversed_lst

            # Expected: [5, 4, 3, 2, 1]
            # Result: [1, 2, 3, 4, 5]
            print(reverse_list([1, 2, 3, 4, 5]))
            """,
            [
                "Replace lst[i] with lst[len(lst) - 1 - i]",
                "Replace range(len(lst)) with range(len(lst) - 1, -1, -1)",
                "Add reversed_lst.reverse() before the return",
                "Change append to insert(0, lst[i])"
            ],
            Language.Of("python"),
            Difficulty.Easy,
            100,
            AnswerKey.Of(
                0,
                "The bug is in the index used to access the elements. By using lst[i], it copies in the same order. The fix is to use lst[len(lst) - 1 - i] to access from the end to the beginning, effectively reversing the list.",
                [
                    "Notice the order in which the elements are being accessed...",
                    "The index i goes from 0 to len(lst)-1. In what order do you need to access to reverse?",
                    "If the list has 5 elements, to reverse it you need to start from index 4 and go to 0."
                ])),

        Question.Create(
            QuestionId.Of("js-001"),
            "Closure Trap",
            "This JavaScript loop should print 0, 1, 2, but it prints 3, 3, 3. Why?",
            """
            for (var i = 0; i < 3; i++) {
              setTimeout(function() {
                console.log(i);
              }, 1000);
            }
            // Expected: 0, 1, 2
            // Result: 3, 3, 3
            """,
            [
                "Replace var with let",
                "Add i = 0 inside the setTimeout",
                "Replace setTimeout with setInterval",
                "Remove the anonymous function"
            ],
            Language.Of("javascript"),
            Difficulty.Medium,
            200,
            AnswerKey.Of(
                0,
                "The classic closure problem with var! The var variable is function-scoped, not block-scoped. When the setTimeout callbacks run, the loop has already finished and i is 3. Replacing var with let creates a block scope for each iteration, preserving the correct value of i.",
                [
                    "Think about when the setTimeout actually runs the callback...",
                    "What is the scope difference between var and let?",
                    "var is function-scoped. When the callbacks run, the loop is already over."
                ])),

        Question.Create(
            QuestionId.Of("ts-001"),
            "Wrong Generic Type",
            "This TypeScript function has a type error. Find the problem!",
            """
            interface User {
              name: string;
              age: number;
            }

            function getProperty<T>(obj: T, key: string) {
              return obj[key]; // Error!
            }

            const user: User = { name: "Ana", age: 25 };
            const name = getProperty(user, "name");
            """,
            [
                "Type key as keyof T instead of string",
                "Add as any after obj[key]",
                "Replace interface with type",
                "Remove the generic T"
            ],
            Language.Of("typescript"),
            Difficulty.Medium,
            200,
            AnswerKey.Of(
                0,
                "TypeScript cannot guarantee that the string passed as key actually exists in T. The solution is to use keyof T to constrain the key parameter to only the valid keys of the object, making the code type-safe.",
                [
                    "TypeScript needs to know that the key exists in the object...",
                    "There is an operator in TypeScript that extracts the keys of a type...",
                    "keyof T returns a union of all the keys of T."
                ])),

        Question.Create(
            QuestionId.Of("java-001"),
            "String Comparison",
            "This Java code compares strings the wrong way. What is the problem?",
            """
            public class Main {
                public static void main(String[] args) {
                    String a = new String("hello");
                    String b = new String("hello");

                    if (a == b) {
                        System.out.println("Equal!");
                    } else {
                        System.out.println("Different!");
                    }
                    // Prints: "Different!"
                    // Expected: "Equal!"
                }
            }
            """,
            [
                "Replace == with .equals()",
                "Use String.compare(a, b)",
                "Replace new String() with direct assignment",
                "Add .toString() before the comparison"
            ],
            Language.Of("java"),
            Difficulty.Easy,
            100,
            AnswerKey.Of(
                0,
                "In Java, the == operator compares references (memory addresses), not the content of the strings. Since a and b are different objects created with new, == returns false. The .equals() method compares the content of the strings, which is the desired behavior.",
                [
                    "In Java, == works differently for objects and primitives...",
                    "When you use new, each call creates a different object in memory...",
                    "To compare the content of objects in Java, use a specific method."
                ])),

        Question.Create(
            QuestionId.Of("cs-001"),
            "Null Reference Exception",
            "This C# code throws a NullReferenceException. Where is the error?",
            """
            public class UserService
            {
                private List<string> _users;

                public void AddUser(string name)
                {
                    _users.Add(name); // NullReferenceException!
                }

                public int GetCount()
                {
                    return _users.Count;
                }
            }
            """,
            [
                "Initialize _users = new List<string>() at the declaration",
                "Add a null check before _users.Add()",
                "Replace List<string> with string[]",
                "Add static to the _users field"
            ],
            Language.Of("csharp"),
            Difficulty.Medium,
            200,
            AnswerKey.Of(
                0,
                "The _users field was declared but never initialized, so its default value is null. Trying to call .Add() on null causes a NullReferenceException. The solution is to initialize the list at the declaration: private List<string> _users = new List<string>();",
                [
                    "What is the default value of a reference field in C#?",
                    "Can you call methods on an object that is null?",
                    "The list needs to exist before you add items to it."
                ])),

        Question.Create(
            QuestionId.Of("cpp-001"),
            "Silent Overflow",
            "This C++ code has an overflow bug. Identify the problem!",
            """
            #include <iostream>
            using namespace std;

            int factorial(int n) {
                int result = 1;
                for (int i = 1; i <= n; i++) {
                    result *= i;
                }
                return result;
            }

            int main() {
                cout << factorial(20) << endl;
                // Result: -2102132736
                // Expected: 2432902008176640000
                return 0;
            }
            """,
            [
                "Replace int with long long in result and the return type",
                "Add unsigned before int",
                "Use double instead of int",
                "Add an overflow check in the loop"
            ],
            Language.Of("cpp"),
            Difficulty.Hard,
            300,
            AnswerKey.Of(
                0,
                "The int type in C++ is typically 32 bits, with a maximum value of ~2.1 billion. The factorial of 20 is ~2.4 quintillion, far beyond that limit. Using long long (64 bits), the maximum value is ~9.2 quintillion, enough for factorial(20).",
                [
                    "What is the maximum value an int can store in C++?",
                    "20! is a VERY large number. How large?",
                    "C++ has integer types larger than int..."
                ])),

        Question.Create(
            QuestionId.Of("sql-001"),
            "Incorrect JOIN",
            "This SQL query returns more rows than expected. What is the problem?",
            """
            -- Tables: users (id, name), orders (id, user_id, total)
            -- We want: total spent per user

            SELECT u.name, SUM(o.total) as total_spent
            FROM users u, orders o
            WHERE u.id = o.user_id;

            -- Missing GROUP BY! Incorrect result.
            """,
            [
                "Add GROUP BY u.name at the end of the query",
                "Replace SUM with COUNT",
                "Add DISTINCT before u.name",
                "Replace WHERE with HAVING"
            ],
            Language.Of("sql"),
            Difficulty.Medium,
            200,
            AnswerKey.Of(
                0,
                "When we use aggregate functions like SUM() together with non-aggregated columns, we need GROUP BY to indicate how to group the results. Without GROUP BY, the database tries to aggregate everything into a single row or returns an error, depending on the DBMS.",
                [
                    "Aggregate functions like SUM() need to know HOW to group...",
                    "You want the total PER user, so you need to group...",
                    "GROUP BY defines which columns determine the aggregation groups."
                ])),

        Question.Create(
            QuestionId.Of("py-002"),
            "Mutable Default",
            "This Python function has unexpected behavior with the default argument. Figure it out!",
            """
            def add_item(item, lst=[]):
                lst.append(item)
                return lst

            # Calls:
            print(add_item("a"))  # ['a'] - OK
            print(add_item("b"))  # ['a', 'b'] - Bug!
            print(add_item("c"))  # ['a', 'b', 'c'] - Bug!
            """,
            [
                "Use None as the default and create the list inside the function",
                "Replace lst=[] with lst=list()",
                "Add lst.clear() at the start",
                "Use *args instead of lst"
            ],
            Language.Of("python"),
            Difficulty.Hard,
            300,
            AnswerKey.Of(
                0,
                "In Python, mutable default arguments (like lists) are evaluated only ONCE when the function is defined, not on each call. The same list is shared across all calls. The idiomatic solution is to use None as the default and create the list inside the function: if lst is None: lst = []",
                [
                    "When exactly does Python evaluate the default values of arguments?",
                    "Mutable objects like lists can be modified in-place...",
                    "The default is created once and reused across all calls."
                ])),

        Question.Create(
            QuestionId.Of("js-002"),
            "Lost Promise",
            "This async function doesn't wait for the result correctly. Where is the bug?",
            """
            async function fetchUsers() {
              const userIds = [1, 2, 3, 4, 5];
              const users = [];

              userIds.forEach(async (id) => {
                const response = await fetch(`/api/users/${id}`);
                const user = await response.json();
                users.push(user);
              });

              console.log(users); // [] - Empty array!
              return users;
            }
            """,
            [
                "Replace forEach with for...of or use Promise.all with map",
                "Add await before userIds.forEach",
                "Replace const users with let users",
                "Move the console.log inside the forEach"
            ],
            Language.Of("javascript"),
            Difficulty.Hard,
            300,
            AnswerKey.Of(
                0,
                "forEach does not wait for async callbacks! It fires off all the Promises but doesn't await any of them. The console.log runs before any fetch finishes. The solution is to use for...of (sequential) or Promise.all(userIds.map(...)) (parallel) to ensure all the Promises are resolved.",
                [
                    "Does forEach know how to handle async functions?",
                    "When does the console.log run relative to the fetches?",
                    "There are better ways to iterate with async/await..."
                ]))
    ];
}
