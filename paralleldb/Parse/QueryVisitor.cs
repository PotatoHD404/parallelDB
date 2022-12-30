﻿using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using ParallelDB.Queries;
using ParallelDB.Tables;
using SqlParser;
using static ParallelDB.Queries.Globals;

namespace ParallelDB.Parse;

internal class NotNullType
{
}

internal class DefaultNode
{
    public dynamic? Value { get; set; }
}

public class QueryVisitor : SQLiteParserBaseVisitor<dynamic?>
{
    private static readonly NotNullType NotNull = new();
    private readonly ParallelDb _db;

    public QueryVisitor(ParallelDb db)
    {
        _db = db;
    }

    public override dynamic? VisitParse(SQLiteParser.ParseContext context)
    {
        if (context.sql_stmt_list().Length == 0)
        {
            return null;
        }

        if (context.sql_stmt_list().Length > 1)
        {
            throw new NotSupportedException("Multiple statements are not supported");
        }

        return VisitSql_stmt_list(context.sql_stmt_list()[0]);
    }

    public override dynamic? VisitSql_stmt_list([NotNull] SQLiteParser.Sql_stmt_listContext context)
    {
        var res = context.sql_stmt();
        if (res.Length > 1)
        {
            throw new NotSupportedException("Multiple statements are not supported");
        }

        if (res.Length == 0)
        {
            throw new NotSupportedException("Empty query");
        }

        return VisitSql_stmt(context.sql_stmt()[0]);
    }

    public override dynamic? VisitSql_stmt([NotNull] SQLiteParser.Sql_stmtContext context)
    {
        if (context.create_table_stmt() != null)
        {
            return VisitCreate_table_stmt(context.create_table_stmt());
        }

        if (context.insert_stmt() != null)
        {
            return VisitInsert_stmt(context.insert_stmt());
        }

        if (context.select_stmt() != null)
        {
            return VisitSelect_stmt(context.select_stmt());
        }

        if (context.update_stmt() != null)
        {
            return VisitUpdate_stmt(context.update_stmt());
        }

        if (context.delete_stmt() != null)
        {
            return VisitDelete_stmt(context.delete_stmt());
        }

        if (context.drop_stmt() != null)
        {
            return VisitDrop_stmt(context.drop_stmt());
        }

        throw new NotSupportedException($"Statement {context.GetText()} is not supported");
    }


    public override dynamic VisitSelect_stmt([NotNull] SQLiteParser.Select_stmtContext context)
    {
        var root = context.select_core();
        if (root.Length > 1)
        {
            throw new NotSupportedException("Multiple select statements are not supported");
        }

        SelectQuery? select = VisitSelect_core(root[0]);
        if (select is null) throw new NotSupportedException("Select query is null");
        if (context.limit_clause() is not null)
        {
            select.Limit(GetValue(context.limit_clause().expr()));
            if (context.limit_clause().offset_clause() is not null)
            {
                select.Offset(GetValue(context.limit_clause().offset_clause().expr()));
            }
        }

        return select;
    }

    public override dynamic VisitSelect_core([NotNull] SQLiteParser.Select_coreContext context)
    {
        SelectQuery select = _db.Select();
        if (context.result_column() is not null)
        {
            foreach (var el in context.result_column())
            {
                select.Project(el.GetText());
            }
        }

        if (context.from_clause()?.table_or_subquery() is not null)
        {
            foreach (SQLiteParser.Table_or_subqueryContext el in context.from_clause().table_or_subquery())
            {
                if (el.table_name() is not null)
                {
                    select.From(el.GetText());
                }
                else if (el.select_stmt() is not null)
                {
                    select.From(VisitSelect_stmt(el.select_stmt()));
                }
                else
                {
                    throw new NotSupportedException("Subquery is not supported");
                }
            }
        }
        else
        {
            throw new NotSupportedException("Select query is null");
        }

        if (context.join_clause() is not null)
        {
            var joins = context.join_clause().join_stmt();
            foreach (var el in joins)
            {
                if (el.join_operator().LEFT() is not null)
                {
                    throw new NotSupportedException("Left join is not supported");
                }

                if (el.join_operator().CROSS() is not null)
                {
                    throw new NotSupportedException("Cross join is not supported");
                }

                if (el.join_operator().NATURAL() is not null)
                {
                    throw new NotSupportedException("Natural join is not supported");
                }
                // Func<IRow, IRow, bool> predicate = (_, _) => true;
                // if (el.join_constraint()?.expr() is not null)
                // {
                //     predicate = (row1, row2) => VisitJoin(el.expr(), row1, row2);
                // }
                // if (el.table_or_subquery().table_name() is not null)
                // {
                //     select.Join(el.table_or_subquery().GetText(), predicate);
                // }
                // else if (el.table_or_subquery().select_stmt() is not null)
                // {
                //     select.Join(VisitSelect_stmt(el.table_or_subquery().select_stmt()), predicate);
                // }
                // else
                // {
                //     throw new NotSupportedException("Subquery is not supported");
                // }
                //
                //
                // if (el.table_name() is not null)
                // {
                //     select.Join(el.GetText());
                // }
                // else if (el.select_stmt() is not null)
                // {
                //     select.Join(VisitSelect_stmt(el.select_stmt()));
                // }
                // else
                // {
                //     throw new NotSupportedException("Subquery is not supported");
                // }
            }
        }

        // if (context.where_expr() is not null)
        // {
        //     select.Where(VisitWhere_expr(context.where_expr()));
        // }

        return select;
    }

    public override dynamic VisitDrop_stmt([NotNull] SQLiteParser.Drop_stmtContext context)
    {
        var tableName = context.any_name().GetText();
        var ifExists = context.IF() is not null && context.EXISTS() is not null;
        var res = _db.Drop();
        var table = _db.GetTable(tableName);
        if (table is null && !ifExists)
        {
            throw new Exception($"Table {tableName} does not exist");
        }

        res.Table(tableName);
        if (ifExists)
        {
            res.IfExists();
        }

        return res;
    }

    public override dynamic VisitDelete_stmt([NotNull] SQLiteParser.Delete_stmtContext context)
    {
        var tableName = context.qualified_table_name().GetText();
        var table = _db.GetTable(tableName);
        if (table is null)
        {
            throw new InvalidOperationException($"Table {tableName} does not exist");
        }

        var res = _db.Delete();
        res.From(tableName);
        Func<IRow, bool>? predicate = null;
        if (context.expr() != null)
        {
            predicate = VisitWhereExpr(context.expr(), table);
        }

        if (predicate is not null)
        {
            res.Where(predicate);
        }

        return res;
    }

    public override dynamic VisitUpdate_stmt(SQLiteParser.Update_stmtContext context)
    {
        var tableName = context.qualified_table_name().table_name().GetText();
        var table = _db.GetTable(tableName);
        if (table == null)
        {
            throw new InvalidOperationException($"Table {tableName} does not exist");
        }

        var res = _db.Update();
        var setClauses = context.set_clause().set_stmt().Select(el => VisitSet_stmt(el, table)).Cast<Action<IRow>>()
            .ToList();
        foreach (var clause in setClauses)
        {
            res = res.Set(clause);
        }

        Func<IRow, bool>? predicate = null;
        if (context.where_clause() is not null)
        {
            predicate = VisitWhereExpr(context.where_clause().expr(), table);
        }

        if (predicate is not null)
        {
            res.Where(predicate);
        }

        return res;
    }
    
    public dynamic? VisitJoinOnExpr(SQLiteParser.ExprContext context, Table table, bool leftChild = false)
    {
        if (context.expr().Length == 1)
        {
            return VisitWhereExpr(context.expr()[0], table);
        }

        if (context.expr() is not null && context.expr().Length == 2)
        {
            var left = VisitWhereExpr(context.expr()[0], table);
            var right = VisitWhereExpr(context.expr()[1], table);
            Func<IRow, IRow, bool> res;
            if (context.ASSIGN() is not null) res = (row1, row2) => left(row1, row2, true) == right(row1, row2);
            else if (context.EQ() is not null) res = (row1, row2) => left(row1, row2, true) == right(row1, row2);
            else if (context.LT() is not null) res = (row1, row2) => left(row1, row2, true) < right(row1, row2);
            else if (context.GT_EQ() is not null) res = (row1, row2) => left(row1, row2, true) >= right(row1, row2);
            else if (context.GT() is not null) res = (row1, row2) => left(row1, row2, true) > right(row1, row2);
            else if (context.LT_EQ() is not null) res = (row1, row2) => left(row1, row2, true) <= right(row1, row2);
            else if (context.PLUS() is not null) res = (row1, row2) => left(row1, row2, true) + right(row1, row2);
            else if (context.MINUS() is not null) res = (row1, row2) => left(row1, row2, true) - right(row1, row2);
            else if (context.STAR() is not null) res = (row1, row2) => left(row1, row2, true) * right(row1, row2);
            else if (context.DIV() is not null) res = (row1, row2) => left(row1, row2, true) / right(row1, row2);
            else if (context.AND() is not null) res = (row1, row2) => left(row1, row2, true) && right(row1, row2);
            else if (context.OR() is not null) res = (row1, row2) => left(row1, row2, true) || right(row1, row2);
            else throw new NotSupportedException($"Operation {context.GetText()} is not supported");
            return res;
        }
        Func<IRow, IRow, IRow> pickRow = (row1, row2) => leftChild ? row1 : row2;

        if (context.expr() is not null && context.expr().Length == 1)
        {
            var expr = VisitWhereExpr(context.expr()[1], table);
            Func<IRow, IRow, bool> res;
            if (context.NOT() is not null) res = (row1, row2) => !expr(pickRow(row1, row2));
            else if (context.MINUS() is not null) res = (row1, row2) => -expr(pickRow(row1, row2));
            else if (context.PLUS() is not null) res = (row1, row2) => +expr(pickRow(row1, row2));
            else if (context.IS() is not null && context.NOT() is not null) res = (row1, row2) => expr(pickRow(row1, row2)) is not null;
            else if (context.IS() is not null) res = (row1, row2) => expr(pickRow(row1, row2)) is null;
            else return VisitJoinOnExpr(context.expr()[0], table);
            return res;
        }

        if (context.expr().Length == 0)
        {
            if (context.literal_value() is not null)
                return new Func<IRow, IRow, dynamic?>((_, _) => GetValue(context.literal_value()));
            if (context.column_name() is not null)
            {
                var columnName = context.column_name().GetText();
                var column = table.GetColumn(columnName);
                if (column == null)
                {
                    throw new InvalidOperationException($"Column {columnName} does not exist");
                }

                return new Func<IRow, IRow, dynamic?>((row1, row2) => pickRow(row1, row2)[columnName]);
            }
        }

        throw new NotSupportedException($"Expression {context.GetText()} is not supported");
    }
    public dynamic? VisitWhereExpr(SQLiteParser.ExprContext context, Table table)
    {
        if (context.expr().Length == 1)
        {
            return VisitWhereExpr(context.expr()[0], table);
        }

        if (context.expr() is not null && context.expr().Length == 2)
        {
            var left = VisitWhereExpr(context.expr()[0], table);
            var right = VisitWhereExpr(context.expr()[1], table);
            Func<IRow, bool> res;
            if (context.ASSIGN() is not null) res = row => left(row) == right(row);
            else if (context.EQ() is not null) res = row => left(row) == right(row);
            else if (context.LT() is not null) res = row => left(row) < right(row);
            else if (context.GT_EQ() is not null) res = row => left(row) >= right(row);
            else if (context.GT() is not null) res = row => left(row) > right(row);
            else if (context.LT_EQ() is not null) res = row => left(row) <= right(row);
            else if (context.PLUS() is not null) res = row => left(row) + right(row);
            else if (context.MINUS() is not null) res = row => left(row) - right(row);
            else if (context.STAR() is not null) res = row => left(row) * right(row);
            else if (context.DIV() is not null) res = row => left(row) / right(row);
            else if (context.AND() is not null) res = row => left(row) && right(row);
            else if (context.OR() is not null) res = row => left(row) || right(row);
            else throw new NotSupportedException($"Operation {context.GetText()} is not supported");
            return res;
        }

        if (context.expr() is not null && context.expr().Length == 1)
        {
            var expr = VisitWhereExpr(context.expr()[1], table);
            Func<IRow, bool> res;
            if (context.NOT() is not null) res = row => !expr(row);
            else if (context.MINUS() is not null) res = row => -expr(row);
            else if (context.PLUS() is not null) res = row => +expr(row);
            else if (context.IS() is not null && context.NOT() is not null) res = row => expr(row) is not null;
            else if (context.IS() is not null) res = row => expr(row) is null;
            else return VisitWhereExpr(context.expr()[0], table);
            return res;
        }

        if (context.expr().Length == 0)
        {
            if (context.literal_value() is not null)
                return new Func<IRow, dynamic?>(_ => GetValue(context.literal_value()));
            if (context.column_name() is not null)
            {
                var columnName = context.column_name().GetText();
                var column = table.GetColumn(columnName);
                if (column == null)
                {
                    throw new InvalidOperationException($"Column {columnName} does not exist");
                }

                return new Func<IRow, dynamic?>(row => row[columnName]);
            }
        }

        throw new NotSupportedException($"Expression {context.GetText()} is not supported");
    }

    public dynamic VisitSet_stmt(SQLiteParser.Set_stmtContext context, Table table)
    {
        var columnName = context.column_name().GetText();
        Column column = table.GetColumn(columnName);
        var value = GetValue(context.expr());
        column.CheckType(value);

        return new Action<IRow>(row => row[columnName] = value);
    }

    public override dynamic VisitInsert_stmt([NotNull] SQLiteParser.Insert_stmtContext context)
    {
        string tableName = context.table_name().GetText();
        var res = _db.Insert();
        Table? table = _db.GetTable(tableName);
        if (table is null)
        {
            throw new Exception($"Table {tableName} does not exist");
        }

        res.Into(tableName);
        var columns = context.columns_clause().column_name().Select(x => x.GetText()).ToArray();
        res.Columns(columns);
        var values = context.values_clause().values_stmt().Select(x => x.expr().Select(GetValue).ToArray()).ToArray();
        foreach (var value in values)
        {
            res.Values(value);
        }

        return res;
    }

    public override dynamic VisitCreate_table_stmt([NotNull] SQLiteParser.Create_table_stmtContext context)
    {
        var tableName = context.table_name().GetText();
        var res = _db.Create();
        res.tableName = tableName;
        res.columns = context.column_def().Select(Visit).Cast<Column>().ToDictionary(x => x.Name, x => x);
        res.ifNotExists = context.IF() is not null && context.NOT() is not null && context.EXISTS() is not null;
        return res;
    }

    public override dynamic VisitColumn_def([NotNull] SQLiteParser.Column_defContext context)
    {
        var name = context.column_name().GetText();
        var typeName = context.type_name().GetText();

        Type type = ToClrType(typeName);
        if (type is null)
        {
            throw new NotSupportedException($"Type {typeName} is not supported");
        }

        // visit column constraints
        var constraints = context.column_constraint().Select(Visit).ToList();
        var isNullable = !constraints.OfType<NotNullType>().Any();
        var defaultValue = constraints.OfType<DefaultNode>().FirstOrDefault()?.Value;
        if (defaultValue is null)
        {
            return new Column(name, type, isNullable);
        }

        return new Column(name, type, isNullable, true, defaultValue);
    }

    private static Type ToClrType(string typeName)
    {
        var type = typeName.ToUpper() switch
        {
            "TINYINT" => typeof(byte),
            "SMALLINT" => typeof(short),
            "MEDIUMINT" => typeof(int),
            "INT" => typeof(int),
            "INTEGER" => typeof(int),
            "BIGINT" => typeof(long),
            "FLOAT" => typeof(float),
            "DOUBLE" => typeof(double),
            "REAL" => typeof(double),
            "BIT" => typeof(bool),
            "BOOL" => typeof(bool),
            "BOOLEAN" => typeof(bool),
            "TEXT" => typeof(string),
            "BLOB" => typeof(byte[]),
            "TIME" => typeof(TimeSpan),
            "DATE" => typeof(DateTime),
            "DATETIME" => typeof(DateTime),
            "TIMESTAMP" => typeof(DateTime),
            _ => throw new NotSupportedException()
        };

        return type;
    }

    public override dynamic VisitColumn_constraint([NotNull] SQLiteParser.Column_constraintContext context)
    {
        var not = context.NOT() is not null;
        var @null = context.NULL() is not null;
        if (not && @null)
        {
            return NotNull;
        }

        var @default = context.DEFAULT() is not null;
        if (!@default)
        {
            throw new NotSupportedException($"Constraint {context.GetText()} is not supported");
        }

        dynamic? value;
        if (context.literal_value() is not null)
        {
            value = GetValue(context.literal_value());
        }
        else if (context.signed_number() is not null)
        {
            value = GetValue(context.signed_number());
        }
        else
        {
            throw new NotSupportedException("Default value is not supported");
        }


        return new DefaultNode { Value = value };
    }

    public dynamic? GetValue([NotNull] ParserRuleContext context)
    {
        switch (context)
        {
            case SQLiteParser.Literal_valueContext literalValueContext:
                return VisitLiteral_value(literalValueContext);
            case SQLiteParser.Signed_numberContext signedNumberContext:
                return VisitSigned_number(signedNumberContext);
            case SQLiteParser.ExprContext anyNameContext:
                if (anyNameContext.column_name() is not null)
                {
                    return VisitAny_name(anyNameContext.column_name().any_name());
                }

                return VisitLiteral_value(anyNameContext.literal_value());
            default:
                throw new NotSupportedException("Value is not supported");
        }
    }

    public override dynamic VisitAny_name([NotNull] SQLiteParser.Any_nameContext context)
    {
        if (context.keyword() is not null)
        {
            if (context.keyword().GetText().ToUpper() == "DEFAULT")
            {
                return Default;
            }

            throw new NotSupportedException($"Keyword {context.GetText()} is not supported");
        }

        return context.GetText();
    }

    public override dynamic? VisitLiteral_value([NotNull] SQLiteParser.Literal_valueContext context)
    {
        if (context.NUMERIC_LITERAL() is not null)
        {
            return context.NUMERIC_LITERAL() switch
            {
                { } n when n.GetText().Contains('.') => double.Parse(n.GetText()),
                { } n => int.Parse(n.GetText())
            };
        }

        if (context.STRING_LITERAL() is not null)
        {
            var res = context.STRING_LITERAL().GetText();
            return res[1..^1];
        }

        if (context.NULL() is not null)
        {
            return null;
        }

        if (context.TRUE() is not null)
        {
            return true;
        }

        if (context.FALSE() is not null)
        {
            return false;
        }

        if (context.BLOB_LITERAL() is not null)
        {
            throw new NotSupportedException("BLOB is not supported");
        }


        throw new NotSupportedException("Literal value is not supported");
    }
}