import http from "node:http";
import Database from "better-sqlite3";
import pino from "pino";

const logger = pino();

const db = new Database("quotes.db");

db.prepare(`
CREATE TABLE IF NOT EXISTS Quotes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Author TEXT NOT NULL,
    Text TEXT NOT NULL
)
`).run();

type Quote = {
    id: number;
    author: string;
    text: string;
};

type CreateQuoteRequest = {
    author: string;
    text: string;
};

let activeRequests = 0;
let shuttingDown = false;

function sendJson(
    res: http.ServerResponse,
    status: number,
    body: unknown
) {
    res.writeHead(status, {
        "Content-Type": "application/json"
    });

    res.end(JSON.stringify(body));
}

async function readBody(
    req: http.IncomingMessage
): Promise<string> {
    return new Promise((resolve, reject) => {
        const chunks: Buffer[] = [];

        req.on("data", chunk => {
            chunks.push(Buffer.from(chunk));
        });

        req.on("end", () => {
            resolve(
                Buffer.concat(chunks).toString("utf8")
            );
        });

        req.on("error", err => {
            reject(err);
        });

        req.on("aborted", () => {
            reject(new Error("Request aborted"));
        });
    });
}

const server = http.createServer(async (req, res) => {
    activeRequests++;

    try {
        if (!req.url || !req.method) {
            sendJson(res, 400, {
                title: "Bad Request"
            });
            return;
        }

        logger.info({
            method: req.method,
            url: req.url
        });

        const url = new URL(req.url, "http://localhost");

        // GET ALL
        if (
            req.method === "GET" &&
            url.pathname === "/api/quotes"
        ) {
            const page =
                Number(url.searchParams.get("page")) || 1;

            const size =
                Number(url.searchParams.get("size")) || 10;

            const offset = (page - 1) * size;

            const quotes = db.prepare(`
                SELECT *
                FROM Quotes
                LIMIT ?
                OFFSET ?
            `).all(size, offset);

            sendJson(res, 200, quotes);

            return;
        }

        // GET BY ID
        if (
            req.method === "GET" &&
            url.pathname.startsWith("/api/quotes/")
        ) {
            const id =
                Number(url.pathname.split("/")[3]);

            const quote = db.prepare(`
                SELECT *
                FROM Quotes
                WHERE Id = ?
            `).get(id);

            if (!quote) {
                sendJson(res, 404, {
                    title: "Not Found"
                });

                return;
            }

            sendJson(res, 200, quote);

            return;
        }

        // POST
        if (
    req.method === "POST" &&
    url.pathname === "/api/quotes"
) {
    const body = await readBody(req);

    console.log("RAW BODY:");
    console.log(body);
    console.log(typeof body);

    let request: CreateQuoteRequest;

    try {
        request =
            JSON.parse(body) as CreateQuoteRequest;
    }
    catch {
        sendJson(res, 400, {
            title: "Invalid JSON"
        });

        return;
    }

    if (
        !request.author ||
        !request.text
    ) {
        sendJson(res, 400, {
            title: "Validation Error",
            errors: {
                author: !request.author
                    ? ["Author required"]
                    : [],
                text: !request.text
                    ? ["Text required"]
                    : []
            }
        });

        return;
    }

    const result = db.prepare(`
        INSERT INTO Quotes (Author, Text)
        VALUES (?, ?)
    `).run(
        request.author,
        request.text
    );

    const quote: Quote = {
        id: Number(result.lastInsertRowid),
        author: request.author,
        text: request.text
    };

    sendJson(res, 201, quote);

    return;
}
        // DELETE
        if (
            req.method === "DELETE" &&
            url.pathname.startsWith("/api/quotes/")
        ) {
            const id =
                Number(url.pathname.split("/")[3]);

            db.prepare(`
                DELETE FROM Quotes
                WHERE Id = ?
            `).run(id);

            res.writeHead(204);

            res.end();

            return;
        }

        sendJson(res, 404, {
            title: "Not Found"
        });
    }
    catch (error) {
        logger.error(error);

        sendJson(res, 500, {
            title: "Server Error"
        });
    }
    finally {
        activeRequests--;
    }
});

server.listen(3000, () => {
    logger.info(
        "Server running at http://localhost:3000"
    );
});

process.on("SIGINT", () => {
    shuttingDown = true;

    logger.info("Shutting down gracefully...");

    server.close(() => {
        db.close();

        logger.info("Server closed.");

        process.exit(0);
    });

    const interval = setInterval(() => {
        if (activeRequests === 0) {
            clearInterval(interval);

            db.close();

            process.exit(0);
        }
    }, 100);
});