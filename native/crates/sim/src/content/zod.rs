//! The subset of zod the content schema uses, as values: `parse` checks a YAML document's value
//! and returns zod's output for it (defaults filled in, unknown keys rejected by strict objects),
//! so the loaded content hashes as the TS loader's does.

use std::fmt::Write as _;

use serde_json::{Map, Value};

/// One validation problem, at a path into the document.
#[derive(Clone, Debug, PartialEq)]
pub struct Issue {
    pub path: Vec<PathPart>,
    pub message: String,
}

#[derive(Clone, Debug, PartialEq)]
pub enum PathPart {
    Key(String),
    Index(usize),
}

pub fn path_string(path: &[PathPart]) -> String {
    let mut s = String::new();
    for p in path {
        match p {
            PathPart::Key(k) => {
                if !s.is_empty() {
                    s.push('.');
                }
                s.push_str(k);
            }
            PathPart::Index(i) => {
                let _ = write!(s, "[{i}]");
            }
        }
    }
    s
}

#[derive(Clone, Debug)]
pub enum Schema {
    Number(Num),
    String(Str),
    Bool,
    /// A string literal.
    Literal(&'static str),
    Enum(Vec<&'static str>),
    Tuple(Vec<Schema>),
    Array {
        item: Box<Schema>,
        min: usize,
    },
    /// A strict object: unknown keys are errors.
    Object(Vec<Field>),
    /// The first variant that parses.
    Union(Vec<Schema>),
    /// Objects told apart by a literal `key` field.
    Discriminated {
        key: &'static str,
        variants: Vec<(&'static str, Schema)>,
    },
    /// `z.record(key, value)`; a key check stands for an id-like key schema.
    Record {
        key: Option<StrCheck>,
        value: Box<Schema>,
    },
    /// `z.unknown()`: anything, passed through.
    Any,
    /// A schema with an extra check of the parsed value.
    Refine(Box<Schema>, fn(&Value) -> Option<String>),
}

#[derive(Clone, Debug, Default)]
pub struct Num {
    pub min: Option<f64>,
    pub max: Option<f64>,
    /// `.positive()`: greater than 0.
    pub positive: bool,
    pub int: bool,
}

/// A check standing for a regex, with its message.
pub type StrCheck = (fn(&str) -> bool, &'static str);

#[derive(Clone, Debug, Default)]
pub struct Str {
    pub check: Option<StrCheck>,
}

#[derive(Clone, Debug)]
pub struct Field {
    pub name: &'static str,
    pub schema: Schema,
    pub optional: bool,
    pub default: Option<Value>,
}

/// A required field.
pub fn req(name: &'static str, schema: Schema) -> Field {
    Field {
        name,
        schema,
        optional: false,
        default: None,
    }
}

/// An optional field (absent stays absent).
pub fn opt(name: &'static str, schema: Schema) -> Field {
    Field {
        name,
        schema,
        optional: true,
        default: None,
    }
}

/// A field with a default (absent becomes the default, as given: zod 4 doesn't parse it).
pub fn def(name: &'static str, schema: Schema, default: Value) -> Field {
    Field {
        name,
        schema,
        optional: false,
        default: Some(default),
    }
}

pub fn parse(schema: &Schema, v: &Value) -> Result<Value, Vec<Issue>> {
    let mut issues = Vec::new();
    let mut path = Vec::new();
    let out = parse_at(schema, v, &mut path, &mut issues);
    if issues.is_empty() {
        Ok(out.unwrap_or(Value::Null))
    } else {
        Err(issues)
    }
}

fn issue(path: &[PathPart], issues: &mut Vec<Issue>, message: impl Into<String>) -> Option<Value> {
    issues.push(Issue {
        path: path.to_vec(),
        message: message.into(),
    });
    None
}

fn type_name(v: &Value) -> &'static str {
    match v {
        Value::Null => "null",
        Value::Bool(_) => "boolean",
        Value::Number(_) => "number",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
}

fn parse_at(
    schema: &Schema,
    v: &Value,
    path: &mut Vec<PathPart>,
    issues: &mut Vec<Issue>,
) -> Option<Value> {
    match schema {
        Schema::Number(n) => {
            let Some(x) = v.as_f64().filter(|_| v.is_number()) else {
                return issue(
                    path,
                    issues,
                    format!("expected number, received {}", type_name(v)),
                );
            };
            if !x.is_finite() {
                return issue(path, issues, "expected a finite number");
            }
            if n.int && x.fract() != 0.0 {
                return issue(path, issues, "expected an integer");
            }
            if n.positive && x <= 0.0 {
                return issue(path, issues, "too small: expected number to be >0");
            }
            if let Some(min) = n.min
                && x < min
            {
                return issue(
                    path,
                    issues,
                    format!("too small: expected number to be >={min}"),
                );
            }
            if let Some(max) = n.max
                && x > max
            {
                return issue(
                    path,
                    issues,
                    format!("too big: expected number to be <={max}"),
                );
            }
            // (Every number is an f64, as in JS.)
            Some(Value::from(x))
        }
        Schema::String(s) => {
            let Some(text) = v.as_str() else {
                return issue(
                    path,
                    issues,
                    format!("expected string, received {}", type_name(v)),
                );
            };
            if let Some((check, message)) = s.check
                && !check(text)
            {
                return issue(path, issues, message);
            }
            Some(v.clone())
        }
        Schema::Bool => {
            if v.is_boolean() {
                Some(v.clone())
            } else {
                issue(
                    path,
                    issues,
                    format!("expected boolean, received {}", type_name(v)),
                )
            }
        }
        Schema::Literal(lit) => {
            if v.as_str() == Some(lit) {
                Some(v.clone())
            } else {
                issue(path, issues, format!("invalid input: expected \"{lit}\""))
            }
        }
        Schema::Enum(options) => match v.as_str() {
            Some(s) if options.contains(&s) => Some(v.clone()),
            _ => issue(
                path,
                issues,
                format!(
                    "invalid option: expected one of {}",
                    options
                        .iter()
                        .map(|o| format!("\"{o}\""))
                        .collect::<Vec<_>>()
                        .join("|")
                ),
            ),
        },
        Schema::Tuple(items) => {
            let Some(a) = v.as_array() else {
                return issue(
                    path,
                    issues,
                    format!("expected tuple, received {}", type_name(v)),
                );
            };
            if a.len() != items.len() {
                return issue(
                    path,
                    issues,
                    format!("expected a tuple of {} items", items.len()),
                );
            }
            let mut out = Vec::new();
            let before = issues.len();
            for (i, (s, x)) in items.iter().zip(a).enumerate() {
                path.push(PathPart::Index(i));
                out.push(parse_at(s, x, path, issues).unwrap_or(Value::Null));
                path.pop();
            }
            (issues.len() == before).then_some(Value::Array(out))
        }
        Schema::Array { item, min } => {
            let Some(a) = v.as_array() else {
                return issue(
                    path,
                    issues,
                    format!("expected array, received {}", type_name(v)),
                );
            };
            if a.len() < *min {
                return issue(
                    path,
                    issues,
                    format!("too small: expected array to have >={min} items"),
                );
            }
            let mut out = Vec::new();
            let before = issues.len();
            for (i, x) in a.iter().enumerate() {
                path.push(PathPart::Index(i));
                out.push(parse_at(item, x, path, issues).unwrap_or(Value::Null));
                path.pop();
            }
            (issues.len() == before).then_some(Value::Array(out))
        }
        Schema::Object(fields) => {
            let Some(obj) = v.as_object() else {
                return issue(
                    path,
                    issues,
                    format!("expected object, received {}", type_name(v)),
                );
            };
            let before = issues.len();
            let mut out = Map::new();
            for f in fields {
                path.push(PathPart::Key(f.name.to_string()));
                match obj.get(f.name) {
                    Some(x) => {
                        if let Some(parsed) = parse_at(&f.schema, x, path, issues) {
                            out.insert(f.name.to_string(), parsed);
                        }
                    }
                    None => {
                        if let Some(d) = &f.default {
                            // (zod 4 returns a default as given, without parsing it.)
                            out.insert(f.name.to_string(), d.clone());
                        } else if !f.optional {
                            issue(path, issues, "required field is missing");
                        }
                    }
                }
                path.pop();
            }
            for key in obj.keys() {
                if !fields.iter().any(|f| f.name == key) {
                    path.push(PathPart::Key(key.clone()));
                    issue(path, issues, format!("unknown field \"{key}\""));
                    path.pop();
                }
            }
            (issues.len() == before).then_some(Value::Object(out))
        }
        Schema::Union(options) => {
            let mut first_issues = None;
            for s in options {
                let mut tried = Vec::new();
                if let Some(out) = parse_at(s, v, path, &mut tried)
                    && tried.is_empty()
                {
                    return Some(out);
                }
                first_issues.get_or_insert(tried);
            }
            issues.extend(first_issues.unwrap_or_default());
            if issues.is_empty() {
                issue(path, issues, "invalid input");
            }
            None
        }
        Schema::Discriminated { key, variants } => {
            let tag = v.get(*key).and_then(Value::as_str);
            if let Some((_, s)) = variants.iter().find(|(name, _)| Some(*name) == tag) {
                parse_at(s, v, path, issues)
            } else {
                path.push(PathPart::Key((*key).to_string()));
                let expected: Vec<String> =
                    variants.iter().map(|(n, _)| format!("\"{n}\"")).collect();
                issue(
                    path,
                    issues,
                    format!(
                        "invalid discriminator value: expected one of {}",
                        expected.join("|")
                    ),
                );
                path.pop();
                None
            }
        }
        Schema::Any => Some(v.clone()),
        Schema::Record { key, value } => {
            let Some(obj) = v.as_object() else {
                return issue(
                    path,
                    issues,
                    format!("expected record, received {}", type_name(v)),
                );
            };
            let before = issues.len();
            let mut out = Map::new();
            for (k, x) in obj {
                path.push(PathPart::Key(k.clone()));
                if let Some((check, message)) = key
                    && !check(k)
                {
                    issue(path, issues, *message);
                    path.pop();
                    continue;
                }
                if let Some(parsed) = parse_at(value, x, path, issues) {
                    out.insert(k.clone(), parsed);
                }
                path.pop();
            }
            (issues.len() == before).then_some(Value::Object(out))
        }
        Schema::Refine(inner, check) => {
            let out = parse_at(inner, v, path, issues)?;
            if let Some(message) = check(&out) {
                return issue(path, issues, message);
            }
            Some(out)
        }
    }
}
