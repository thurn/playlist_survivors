"""Command-line entry point."""

import argparse


def main() -> None:
    parser = argparse.ArgumentParser(prog="ps-analysis")
    parser.add_subparsers(dest="command", required=True)
    parser.parse_args()
