1) What did the rich model buy you that the anemic one didn't? Be specific?

-In my anemic model the validations were done at endpoints which might create issues so I    validated the rules at creation itself. 
-Earlier in anemic model public setters were present. Claude gave suggestion to include private setters instead of public setters so that the users csn only read it ,not write it.
-Soft delete was not done in anemic model but in rich model soft delete in done so that the entry is stored in database for auditing logs but at the same time not visible to the end user.
-Validation erros would occur in anemic models whereas in rich model the middleware throws the named exception if any.

2) Include one scenario where the anemic version would have shipped a bug the rich one catches.
If in future some uploads large number of quotes from a csv file at a time. And that file contains empty quotes, or legnth larger than 5000 or any other non standard format, then also the  anemic model saves all of them without complaint. 847 invalid quotes get imported into production. The app starts showing blank author names and truncated text everywhere. 
The rich model uses Quote.Create() method which is a systematic approach for this scenario. 
When it hits the first empty author, QuoteAuthorInvalidException is thrown immediately. The import stops. The developer sees exactly which row failed and why. No invalid data reaches the database.
The rule doesn't care that this is an import script, not an HTTP endpoint. It doesn't care that it's a background job, a migration tool, or a test fixture. Every path that creates a Quote goes through the same validation because there is only one path Quote.Create().
